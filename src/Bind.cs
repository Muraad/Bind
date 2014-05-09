//
//  Copyright 2013-2014 Frank A. Krueger
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.ComponentModel;

namespace Praeclarum.Bind
{
    #region Bind static class, main api entry point.

    // TODO: Maybe rename it to an other name? Is "Bind" too common? 
    // TODO: "PBind" ?!
    public static class Bind
    {
        /// <summary>
        /// Usage is:
        ///     Bind.NotifyPropertyChanged( () => model.Property));
        /// where model is an object that implements INotifyPropertyChanged.
        /// My idea is that the Bind class could function as a main point to trigger all property changed propagation stuff.
        /// If it is used in a WPF application this will simply trigger the "normal" mvvm mechanisms.
        /// If bindings where created with Bind.Create and the binding has hooked into the PropertyChanged 
        /// event handler (when one target side is INotifyPropertyChanged) then this method will
        /// trigger the internal propagation. 
        /// </summary>
        /// <typeparam name="T">Type of the target object. Must be INotifyPropertyChanged.</typeparam>
        /// <param name="propertyExpr">Expression of the form () => model.Property.</param>
        /// <param name="pred">Currently unused.</param>
        /// <returns>True if PropertyChanged was successfully triggered on the target object of type T,
        /// false otherwise.</returns>
        public static bool NotifyPropertyChanged<T>(Expression<Func<T>> propertyExpr, Func<bool> pred = null)
            where T : INotifyPropertyChanged
        {
            bool result = false;
            T target = default(T);

            var memberExpr = propertyExpr.Body as MemberExpression;

            if (memberExpr == null)
                throw new ArgumentException("Given expression is not a MemberExpression", "outExpr");

            // propertyExpr is () => target.Property
            // we need the target here.
            target = (T)Evaluator.EvalExpression(memberExpr);

            if (target == null)
                throw new ArgumentException("Target to call PropertyChanged is null", "target");

            var property = memberExpr.Member as PropertyInfo;
            if (property == null)
                throw new ArgumentException("Given expression member is not a PropertyInfo", "outExpr");

            var propChangedArgs = new PropertyChangedEventArgs(property.Name);

            // Event info is always not null because the constraint on T is INotifyPropertyChanged
            var eventInfo = target.GetType().GetRuntimeEvent("PropertyChanged");

            // TODO: God or bad to use try? Better let the exception fall through?
            try
            {
                // like target.PropertyChanged(target, propChangedArgs)
                eventInfo.RaiseMethod.Invoke(target, new object[] { target, propChangedArgs });
            }
            catch
            {
                result = false;
            }
            return result;
        }

        // UNUSED:
        /*
        public static bool NotifyPropertyChanged<T>(this T target, Expression<Func<T>> propertyExpr, Func<bool> pred = null)
            where T : INotifyPropertyChanged
        {
            bool result = false;

            if (target == null)
                throw new ArgumentException("Target to call PropertyChanged is null", "target");

            var expr = propertyExpr.Body as MemberExpression;

            if (expr == null)
                throw new ArgumentException("Given expression is not a MemberExpression", "outExpr");

            var property = expr.Member as PropertyInfo;
            if (property == null)
                throw new ArgumentException("Given expression member is not a PropertyInfo", "outExpr");

            var propChangedArgs = new PropertyChangedEventArgs(property.Name);

            // Event info is always not null because the constraint on T is INotifyPropertyChanged
            var eventInfo = target.GetType().GetRuntimeEvent("PropertyChanged");

            try
            {
                // like target.PropertyChanged(target, propChangedArgs)
                eventInfo.RaiseMethod.Invoke(target, new object[] { target, propChangedArgs });
            }
            catch
            {
                result = false;
            }
            return result;
        }*/


        #region Static Create, BindExpression and SetValue

        /// <summary>
        /// Uses the lambda expression to create data bindings.
        /// Equality expression (==) become data bindings.
        /// And expressions (&&) can be used to group the data bindings.
        /// </summary>
        /// <param name="specifications">The binding specifications.</param>
        public static Binding Create<T>(Expression<Func<T>> specifications)
        {
            return BindExpression(specifications.Body);
        }

        static Binding BindExpression(Expression expr)
        {
            //
            // Is this a group of bindings
            //
            if (expr.NodeType == ExpressionType.AndAlso)
            {

                var b = (BinaryExpression)expr;

                var parts = new List<Expression>();

                while (b != null)
                {
                    var l = b.Left;
                    parts.Add(b.Right);
                    if (l.NodeType == ExpressionType.AndAlso)
                    {
                        b = (BinaryExpression)l;
                    }
                    else
                    {
                        parts.Add(l);
                        b = null;
                    }
                }

                parts.Reverse();

                return new MultipleBindings(parts.Select(BindExpression));
            }

            //
            // Are we binding two values?
            //
            if (expr.NodeType == ExpressionType.Equal)
            {
                var b = (BinaryExpression)expr;
                return new EqualityBinding(b.Left, b.Right);
            }

            //
            // This must be a new object binding (a template)
            //
            throw new NotSupportedException("Only equality bindings are supported.");
        }

        internal static bool SetValue(Expression expr, object value, int changeId)
        {
            if (expr.NodeType == ExpressionType.MemberAccess)
            {
                var m = (MemberExpression)expr;
                var mem = m.Member;

                var target = Evaluator.EvalExpression(m.Expression);

                var f = mem as FieldInfo;
                var p = mem as PropertyInfo;

                if (f != null)
                {
                    f.SetValue(target, value);
                }
                else if (p != null)
                {
                    p.SetValue(target, value, null);
                }
                else
                {
                    ReportError("Trying to SetValue on " + mem.GetType() + " member");
                    return false;
                }

                InvalidateMember(target, mem, changeId);
                return true;
            }

            ReportError("Trying to SetValue on " + expr.NodeType + " expression");
            return false;
        }

        #endregion

        #region Global error handling

        public static event Action<string> Error = delegate { };

        static void ReportError(string message)
        {
            Debug.WriteLine(message);
            Error(message);
        }

        static void ReportError(object errorObject)
        {
            ReportError(errorObject.ToString());
        }

        #endregion

        #region objectSubs field, internal static AddMemberChangeAction/RemoveMemberChangeAction/InvalidateMember

        static readonly Dictionary<Tuple<Object, MemberInfo>, MemberActions> objectSubs = 
            new Dictionary<Tuple<Object, MemberInfo>, MemberActions>();

        internal static MemberChangeAction AddMemberChangeAction(object target, MemberInfo member, Action<int> k)
        {
            var key = Tuple.Create(target, member);
            MemberActions subs;
            if (!objectSubs.TryGetValue(key, out subs))
            {
                subs = new MemberActions(target, member);
                objectSubs.Add(key, subs);
            }

            //			Debug.WriteLine ("ADD CHANGE ACTION " + target + " " + member);
            var sub = new MemberChangeAction(target, member, k);
            subs.AddAction(sub);
            return sub;
        }

        internal static void RemoveMemberChangeAction(MemberChangeAction sub)
        {
            var key = Tuple.Create(sub.Target, sub.Member);
            MemberActions subs;
            if (objectSubs.TryGetValue(key, out subs))
            {
                //				Debug.WriteLine ("REMOVE CHANGE ACTION " + sub.Target + " " + sub.Member);
                subs.RemoveAction(sub);
            }
        }

        /// <summary>
        /// Invalidate the specified object member. This will cause all actions
        /// associated with that member to be executed.
        /// This is the main mechanism by which binding values are distributed.
        /// </summary>
        /// <param name="target">Target object</param>
        /// <param name="member">Member of the object that changed</param>
        /// <param name="changeId">Change identifier</param>
        public static void InvalidateMember(object target, MemberInfo member, int changeId = 0)
        {
            var key = Tuple.Create(target, member);
            MemberActions subs;
            if (objectSubs.TryGetValue(key, out subs))
            {
                //				Debug.WriteLine ("INVALIDATE {0} {1}", target, member.Name);
                subs.Notify(changeId);
            }
        }

        #endregion
    }

    #endregion


    #region Abstract Binding class

    /// <summary>
	/// Abstract class that represents bindings between values in an applications.
	/// Binding are created using Create and removed by calling Unbind.
	/// </summary>
	public abstract class Binding
	{
		/// <summary>
		/// Unbind this instance. This cannot be undone.
		/// </summary>
		public virtual void Unbind ()
		{
		}
    }

    #endregion

    #region MemberActions class for internal usage only

    /// <summary>
    /// This class stores everything needed to bind to an object property.
    /// Internally it has a list of MemberChangedAction´s.
    /// </summary>
    internal class MemberActions
    {
        #region Static GetEvent() (EventInfo) for type and event name, and CreateGenericEventHandler() (delegate) from EventInfo and action
        
        static EventInfo GetEvent(Type type, string eventName)
        {
            var t = type;
            while (t != null && t != typeof(object))
            {
                var ti = t.GetTypeInfo();
                var ev = t.GetTypeInfo().GetDeclaredEvent(eventName);
                if (ev != null)
                    return ev;
                t = ti.BaseType;
            }
            return null;
        }

        static Delegate CreateGenericEventHandler(EventInfo evt, Action d)
        {
            var handlerType = evt.EventHandlerType;
            var handlerTypeInfo = handlerType.GetTypeInfo();
            var handlerInvokeInfo = handlerTypeInfo.GetDeclaredMethod("Invoke");
            var eventParams = handlerInvokeInfo.GetParameters();

            //lambda: (object x0, EventArgs x1) => d()
            var parameters = eventParams.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var body = Expression.Call(Expression.Constant(d), d.GetType().GetTypeInfo().GetDeclaredMethod("Invoke"));
            var lambda = Expression.Lambda(body, parameters);

            return lambda.Compile();
        }

        #endregion

        #region Private variables target, member, eventInfo, eventHandlers and actions list

        // target and member are also used as a "key" for a Dictionary
        readonly object target;
        readonly MemberInfo member;

        EventInfo eventInfo;
        Delegate eventHandler;
        readonly List<MemberChangeAction> actions = new List<MemberChangeAction>();

        #endregion

        public MemberActions(object target, MemberInfo mem)
        {
            this.target = target;
            member = mem;
        }

        void AddChangeNotificationEventHandler()
        {
            if (target != null)
            {
                var npc = target as INotifyPropertyChanged;
                if (npc != null && (member is PropertyInfo))
                {
                    npc.PropertyChanged += HandleNotifyPropertyChanged;
                }
                else
                {
                    AddHandlerForFirstExistingEvent(member.Name + "Changed", "EditingDidEnd", "ValueChanged", "Changed");
                    //						if (!added) {
                    //							Debug.WriteLine ("Failed to bind to change event for " + target);
                    //						}
                }
            }
        }

        bool AddHandlerForFirstExistingEvent(params string[] names)
        {
            var type = target.GetType();
            foreach (var name in names)
            {
                var ev = GetEvent(type, name);

                if (ev != null)
                {
                    eventInfo = ev;
                    var isClassicHandler = typeof(EventHandler).GetTypeInfo().IsAssignableFrom(ev.EventHandlerType.GetTypeInfo());

                    eventHandler = isClassicHandler ?
                        (EventHandler)HandleAnyEvent :
                        CreateGenericEventHandler(ev, () => HandleAnyEvent(null, EventArgs.Empty));

                    ev.AddEventHandler(target, eventHandler);
                    Debug.WriteLine("BIND: Added handler for {0} on {1}", eventInfo.Name, target);
                    return true;
                }
            }
            return false;
        }

        void UnsubscribeFromChangeNotificationEvent()
        {
            var npc = target as INotifyPropertyChanged;
            if (npc != null && (member is PropertyInfo))
            {
                npc.PropertyChanged -= HandleNotifyPropertyChanged;
                return;
            }

            if (eventInfo == null)
                return;

            eventInfo.RemoveEventHandler(target, eventHandler);

            Debug.WriteLine("BIND: Removed handler for {0} on {1}", eventInfo.Name, target);

            eventInfo = null;
            eventHandler = null;
        }

        void HandleNotifyPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == member.Name)
                Bind.InvalidateMember(target, member);
        }

        void HandleAnyEvent(object sender, EventArgs e)
        {
            Bind.InvalidateMember(target, member);
        }

        /// <summary>
        /// Add the specified action to be executed when Notify() is called.
        /// </summary>
        /// <param name="action">Action.</param>
        public void AddAction(MemberChangeAction action)
        {
            if (actions.Count == 0)
            {
                AddChangeNotificationEventHandler();
            }

            actions.Add(action);
        }

        public void RemoveAction(MemberChangeAction action)
        {
            actions.Remove(action);

            if (actions.Count == 0)
            {
                UnsubscribeFromChangeNotificationEvent();
            }
        }

        /// <summary>
        /// Execute all the actions.
        /// </summary>
        /// <param name="changeId">Change identifier.</param>
        public void Notify(int changeId)
        {
            foreach (var s in actions)
            {
                s.Notify(changeId);
            }
        }
    }

    #endregion

    #region MemberChangeAction class for internal usage only

    /// <summary>
	/// An action tied to a particular member of an object.
	/// When Notify is called, the action is executed.
	/// </summary>
	internal class MemberChangeAction
	{
		readonly Action<int> action;

		public object Target { get; private set; }
		public MemberInfo Member { get; private set; }

		public MemberChangeAction (object target, MemberInfo member, Action<int> action)
		{
			Target = target;
			if (member == null)
				throw new ArgumentNullException ("member");
			Member = member;
			if (action == null)
				throw new ArgumentNullException ("action");
			this.action = action;
		}

		public void Notify (int changeId)
		{
			action (changeId);
		}
	}

    #endregion

    #region Static linq expression evaluator class

    /// <summary>
	/// Methods that can evaluate Linq expressions.
	/// </summary>
	static class Evaluator
	{
		/// <summary>
		/// Gets the value of a Linq expression.
		/// </summary>
		/// <param name="expr">The expresssion.</param>
		public static object EvalExpression (Expression expr)
		{
			//
			// Easy case
			//
			if (expr.NodeType == ExpressionType.Constant) {
				return ((ConstantExpression)expr).Value;
			}
			
			//
			// General case
			//
//			Debug.WriteLine ("WARNING EVAL COMPILED {0}", expr);
			var lambda = Expression.Lambda (expr, Enumerable.Empty<ParameterExpression> ());
			return lambda.Compile ().DynamicInvoke ();
		}
	}

    #endregion

    #region Equality and multiple bindings class

    /// <summary>
	/// Binding between two values. When one changes, the other
	/// is set.
	/// </summary>
	class EqualityBinding : Binding
	{
		object Value;

		class Trigger
		{
			public Expression Expression;
			public MemberInfo Member;
			public MemberChangeAction ChangeAction;
		}
		
		readonly List<Trigger> leftTriggers = new List<Trigger> ();
		readonly List<Trigger> rightTriggers = new List<Trigger> ();
		
		public EqualityBinding (Expression left, Expression right)
		{
			// Try evaling the right and assigning left
			Value = Evaluator.EvalExpression (right);
			var leftSet = Bind.SetValue (left, Value, nextChangeId);

			// If that didn't work, then try the other direction
			if (!leftSet) {
				Value = Evaluator.EvalExpression (left);
                Bind.SetValue(right, Value, nextChangeId);
			}

			nextChangeId++;

			CollectTriggers (left, leftTriggers);
			CollectTriggers (right, rightTriggers);

			Resubscribe (leftTriggers, left, right);
			Resubscribe (rightTriggers, right, left);
		}

		public override void Unbind ()
		{
			Unsubscribe (leftTriggers);
			Unsubscribe (rightTriggers);
			base.Unbind ();
		}

		void Resubscribe (List<Trigger> triggers, Expression expr, Expression dependentExpr)
		{
			Unsubscribe (triggers);
			Subscribe (triggers, changeId => OnSideChanged (expr, dependentExpr, changeId));
		}

		int nextChangeId = 1;
		readonly HashSet<int> activeChangeIds = new HashSet<int> ();
		
		void OnSideChanged (Expression expr, Expression dependentExpr, int causeChangeId)
		{
			if (activeChangeIds.Contains (causeChangeId))
				return;

			var v = Evaluator.EvalExpression (expr);
			
			if (v == null && Value == null)
				return;
			
			if ((v == null && Value != null) ||
				(v != null && Value == null) ||
				((v is IComparable) && ((IComparable)v).CompareTo (Value) != 0)) {
				
				Value = v;

				var changeId = nextChangeId++;
				activeChangeIds.Add (changeId);
                Bind.SetValue(dependentExpr, v, changeId);
				activeChangeIds.Remove (changeId);
			} 
//			else {
//				Debug.WriteLine ("Prevented needless update");
//			}
		}

		static void Unsubscribe (List<Trigger> triggers)
		{
			foreach (var t in triggers) {
				if (t.ChangeAction != null) {
                    Bind.RemoveMemberChangeAction(t.ChangeAction);
				}
			}
		}
		
		static void Subscribe (List<Trigger> triggers, Action<int> action)
		{
			foreach (var t in triggers) {
                t.ChangeAction = Bind.AddMemberChangeAction(Evaluator.EvalExpression(t.Expression), t.Member, action);
			}
		}		
		
		void CollectTriggers (Expression s, List<Trigger> triggers)
		{
			if (s.NodeType == ExpressionType.MemberAccess) {
				
				var m = (MemberExpression)s;
				CollectTriggers (m.Expression, triggers);
				var t = new Trigger { Expression = m.Expression, Member = m.Member };
				triggers.Add (t);

			} else {
				var b = s as BinaryExpression;
				if (b != null) {
					CollectTriggers (b.Left, triggers);
					CollectTriggers (b.Right, triggers);
				}
			}
		}
	}


	/// <summary>
	/// Multiple bindings grouped under a single binding to make adding and removing easier.
	/// </summary>
	class MultipleBindings : Binding
	{
		readonly List<Binding> bindings;

		public MultipleBindings (IEnumerable<Binding> bindings)
		{
			this.bindings = bindings.Where (x => x != null).ToList ();
		}

		public override void Unbind ()
		{
			base.Unbind ();
			foreach (var b in bindings) {
				b.Unbind ();
			}
			bindings.Clear ();
		}
	}

    #endregion

    #region IOS hack

	#if __IOS__
	[MonoTouch.Foundation.Preserve]
	static class PreserveEventsAndSettersHack
	{
		[MonoTouch.Foundation.Preserve]
		static void Hack ()
		{
			var l = new MonoTouch.UIKit.UILabel ();
			l.Text = l.Text + "";

			var tf = new MonoTouch.UIKit.UITextField ();
			tf.Text = tf.Text + "";
			tf.EditingDidEnd += delegate {};
			tf.ValueChanged += delegate {};

			var vc = new MonoTouch.UIKit.UIViewController ();
			vc.Title = vc.Title + "";
			vc.Editing = !vc.Editing;
		}
	}
	#endif

    #endregion

}

