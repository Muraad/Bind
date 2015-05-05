// Based on Copyright 2013-2014 Frank A. Krueger
// Copyright 2014 - Muraad Nofal

using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.ComponentModel;


namespace Bind
{
    /// <summary>
    /// NBind class for creating bindings between propertys
    /// using expressions.
    /// </summary>
    /// <remarks>
    /// Based on https://github.com/praeclarum/Bind. 
    /// Heavily refactored and simplified.
    /// </remarks>
    public static class NBind
    {
        /// <summary>
        /// Create a binding from given expression
        /// </summary>
        /// <typeparam name="T">The binding expression result type (Is always bool)</typeparam>
        /// <param name="bindingExpression">The binding expression</param>
        /// <returns>An IDisposable that can be used to remove the binding when Dispose() is called</returns>
        public static IDisposable Bind<T>(Expression<Func<T>> bindingExpression)
        {
            var bindings = DisposableBindingsFromAndAlsoExpressions(GetAndAlsoExpressions(bindingExpression.Body));
            return Disposable.CreateContainer(bindings.ToArray());
        }

        /// <summary>
        /// Creates a binding and returns a new IDisposable
        /// that when called is disposing the binding and the given disposable
        /// at once.
        /// </summary>
        /// <typeparam name="T">The binding expression result type (Is always bool)</typeparam>
        /// <param name="disposable">The disposable</param>
        /// <param name="bindingExpression">The binding expression</param>
        /// <returns>An IDisposable that can be used to remove the binding and to dispose the given IDisposable at once/returns>
        public static IDisposable Bind<T>(this IDisposable disposable, Expression<Func<T>> bindingExpression)
        {
            IDisposable binding = Bind<T>(bindingExpression);
            return Disposable.CreateContainer(binding, disposable);
        }

        #region Private

        #region Break down Expression into AndAlso List<Expression> and then create bindings

        /// <summary>
        /// Split the given Expression into seperated AndAlso (&&) expressions.
        /// </summary>
        /// <param name="expr">The untyped Expression.</param>
        /// <returns>The given Expression split into expressions seperated by AndAlso (&&).</returns>
        static List<Expression> GetAndAlsoExpressions(Expression expr)
        {
            var parts = new List<Expression>();

            if (expr.NodeType == ExpressionType.AndAlso)
            {
                var b = (BinaryExpression)expr;

                SplitAndAlsoExpressions(parts, b);

                // The parse process was from right (end) to left (start).
                // So reverse the expression list.
                parts.Reverse();

                parts.Select(GetAndAlsoExpressions).ToArray().ForEach(parts.AddRange);
            }
            return parts;
        }

        static void SplitAndAlsoExpressions(List<Expression> parts, BinaryExpression b)
        {
            // split all expressions
            while (b != null)
            {
                // get left part of the binary expression.
                var l = b.Left;

                // the right part have to be also non complex type! See readme at github.
                // So just add it to the expression part list.
                parts.Add(b.Right);

                // If this is again no "==" expression, then get the next binary expr
                // and start again.
                if (l.NodeType == ExpressionType.AndAlso)
                    b = (BinaryExpression)l;
                else
                {
                    // horray, the end is reached, we have the last "==" expression.
                    parts.Add(l);
                    b = null;
                }
            }
        }

        /// <summary>
        /// Creates a binding (IDisposable) for every given seperated Expression that is of type ExpressionType.Equal
        /// </summary>
        /// <remarks>
        /// Takes results from GetAndAlsoExpressions(..)
        /// </remarks>
        /// <param name="parts">The given expressions.</param>
        /// <returns>The a list of IDisposables, one for every created binding, that can be used for unbinding.</returns>
        static List<IDisposable> DisposableBindingsFromAndAlsoExpressions(IEnumerable<Expression> parts)
        {
            List<IDisposable> result = new List<IDisposable>();
            foreach (var part in parts)
            {
                if (part.NodeType == ExpressionType.Equal)
                {
                    var b = (BinaryExpression)part;
                    result.Add(CreateBinding(b.Left, b.Right));
                }
            }
            return result;
        }

        #endregion

        #region Create IDisposable binding (simple or complex) from left and right expression

        /// <summary>
        /// Creates a binding from given left and right expression parts.
        /// </summary>
        /// <remarks>
        /// Parts are from an ExpressionType.Equal.
        /// Left expression have to be an ExpressionType.MemberAccess!
        /// But thats not that hard. Compiler/TypeChecker is taking care this cannot happen.
        /// </remarks>
        /// <param name="left">The left expression. Note type have to be ExpressionType.MemberAccess</param>
        /// <param name="right">The right (complex) expression</param>
        /// <returns>An IDisposable representing the created binding. Dispose for unbinding. Null if binding not created.</returns>
        static IDisposable CreateBinding(Expression left, Expression right)
        {
            if (left.NodeType != ExpressionType.MemberAccess)
                throw new ArgumentException("NOT ALLOWED");

            IDisposable result = null;
            if (right.NodeType == ExpressionType.MemberAccess)   // right is simple too
                result = CreateSimpleBinding(left, right);
            else
                result = CreateComplex(left, right);
            return result;
        }

        #region Create simple binding

        static IDisposable CreateSimpleBinding(Expression left, Expression right)
        {
            BindingMember leftB = BindingMember.FromExpression(left as MemberExpression);
            BindingMember rightB = BindingMember.FromExpression(right as MemberExpression);

            DisposableContainer disposable = new DisposableContainer();
            // left GetMethod is available and right set method too
            // -> when left property changes then set right property
            CreateSimpleLeftToRight(leftB, rightB, disposable);
            CreateSimpleRightToLeft(leftB, rightB, disposable);
            return disposable;
        }

        private static void CreateSimpleRightToLeft(BindingMember leftB, BindingMember rightB, DisposableContainer disposable)
        {
            if (leftB.SetMethod != null && rightB.GetMethod != null)
            {
                disposable.AddDisposable(AddChangeNotificationEventHandler(rightB.Target, rightB.PropertyName, () =>
                {
                    leftB.SetMethod(rightB.GetMethod());
                }));
            }
        }

        private static void CreateSimpleLeftToRight(BindingMember leftB, BindingMember rightB, DisposableContainer disposable)
        {
            if (leftB.GetMethod != null && rightB.SetMethod != null)
            {
                disposable.AddDisposable(AddChangeNotificationEventHandler(leftB.Target, leftB.PropertyName, () =>
                {
                    rightB.SetMethod(leftB.GetMethod());
                }));
            }
        }

        #endregion

        #region Create complex binding

        static IDisposable CreateComplex(Expression left, Expression right)
        {
            DisposableContainer disposable = new DisposableContainer();
            var leftMemEx = left as MemberExpression;

            // Get the instance the property belongs too
            object leftTarget = NBind.EvalExpression(leftMemEx.Expression);
            // Get the property name
            string propertyName = leftMemEx.Member.Name;

            // Get a setter function for the property
            var propInfo = leftTarget.GetType().GetTypeInfo().DeclaredProperties.First(p => p.Name == propertyName);
            Action<object, object> setter = (target, value) => propInfo.SetMethod.Invoke(target, new object[] { value });

            // Create an action that is called whenever one of the propertys in the right expression is changing
            Action rightChangedAction = () => setter(leftTarget, (NBind.EvalExpression(right)));

            //Get all right side triggers and create a complex binding (IDisposable) from it
            return SubscribeToRightSidePropertys(rightChangedAction, RightTriggerFromComplexExpression(right));
        }

        static IDisposable SubscribeToRightSidePropertys(
            Action rightChangedAction, List<Tuple<Expression, MemberInfo>> rightTrigger)
        {
            Type typeNotifyPropertyChanged = typeof(INotifyPropertyChanged);
            DisposableContainer disposable = new DisposableContainer();
            // For all propertys on the right side
            foreach (var expr in rightTrigger)
            {
                // IF this is a property access expression
                if (expr.Item1.NodeType == ExpressionType.MemberAccess)
                {
                    var memEx = expr.Item1 as MemberExpression;

                    // Get the type this property belongs too and check if it is implementing INotifyPropertyChanged
                    if (typeNotifyPropertyChanged.GetTypeInfo().IsAssignableFrom(memEx.Type.GetTypeInfo()))
                    {
                        // Register at PropertyChangedEventHandler and add the unsubscribing IDisposable to the container.
                        disposable.AddDisposable(
                            AddChangeNotificationEventHandler(
                                NBind.EvalExpression(memEx),     // get instance that declares the current right side property
                                expr.Item2.Name,            // the property name where to subscribe to property changed
                                rightChangedAction));       // the action that is updating the left side property when right side changes
                    }
                }
            }
            return disposable;
        }

        #endregion

        #endregion

        #region AddChangeNotificationEventHandler action to target with given property name

        static IDisposable AddChangeNotificationEventHandler(object target, string propertyName, Action action)
        {
            IDisposable binding = null;
            var npc = target as INotifyPropertyChanged;
            if (npc != null)
            {
                PropertyChangedEventHandler handler = (obj, args) =>
                {
                    if (args.PropertyName == propertyName)
                        action();
                };
                npc.PropertyChanged += handler;
                binding = Disposable.Create(() => npc.PropertyChanged -= handler);
            }
            return binding;
        }

        #endregion

        #region Get left and right triggers from simple and complex expressions

        static Tuple<Expression, MemberInfo> LeftTriggerFromMemberExpression(Expression expr)
        {
            //This expression represents a field or property of an instance.
            var m = (MemberExpression)expr;
            return Tuple.Create(m.Expression, m.Member);
        }

        static List<Tuple<Expression, MemberInfo>> RightTriggerFromComplexExpression(Expression expr, List<Tuple<Expression, MemberInfo>> ts = null)
        {
            List<Tuple<Expression, MemberInfo>> triggers = ts == null ? new List<Tuple<Expression, MemberInfo>>() : ts;
            GetBindingMembers(expr, triggers);
            return triggers;
        }

        static void GetBindingMembers(Expression s, List<Tuple<Expression, MemberInfo>> triggers)
        {
            if (s.NodeType == ExpressionType.MemberAccess)
                triggers.Add(LeftTriggerFromMemberExpression(s));
            else
            {
                var b = s as BinaryExpression;
                if (b != null)
                {
                    GetBindingMembers(b.Left, triggers);
                    GetBindingMembers(b.Right, triggers);
                }
            }
        }

        #endregion

        #endregion

        #region Private class BindingMember for simple binding case

        class BindingMember
        {
            public static BindingMember FromExpression(MemberExpression memEx)
            {
                object target = NBind.EvalExpression(memEx.Expression);
                string propName = memEx.Member.Name;
                PropertyInfo propInfo = target.GetType().GetTypeInfo().DeclaredProperties.First(p => p.Name == propName);

                return new BindingMember()
                {
                    Target = target,
                    PropertyName = propName,
                    GetMethod = () => propInfo.GetMethod.Invoke(target, null), //  propInfo.GetAccessor<object>(target),
                    SetMethod = obj => propInfo.SetMethod.Invoke(target, new object[] { obj })  //propInfo.SetAccessor<object>(target)
                };
            }

            public object Target { get; set; }
            public string PropertyName { get; set; }
            public Func<object> GetMethod { get; set; }
            public Action<object> SetMethod { get; set; }
        }

        #endregion

        public static object EvalExpression(Expression operation)
        {
            object value;
            if (!TryEvaluate(operation, out value))
            {
                // use compile / invoke as a fall-back
                value = Expression.Lambda(operation).Compile().DynamicInvoke();
            }
            return value;
        }

        private static bool TryEvaluate(Expression operation, out object value)
        {
            if (operation == null)
            {   // used for static fields, etc
                value = null;
                return true;
            }
            switch (operation.NodeType)
            {
                case ExpressionType.Constant:
                    value = ((ConstantExpression)operation).Value;
                    return true;
                case ExpressionType.MemberAccess:
                    MemberExpression me = (MemberExpression)operation;
                    object target;
                    if (TryEvaluate(me.Expression, out target))
                    { // instance target
                        if (me.Member is FieldInfo)
                        {
                            value = ((FieldInfo)me.Member).GetValue(target);
                            return true;
                        }
                        else if (me.Member is PropertyInfo)
                        {
                            value = ((PropertyInfo)me.Member).GetValue(target, null);
                            return true;
                        }
                    }
                    break;
            }
            value = null;
            return false;
        }
    }

    #region Helper 

    public static class EnumerableExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
                action(item);
        }
    }
    public interface IDisposableContainer : IDisposable
    {
        List<IDisposable> Disposables { get; }
    }

    public static class IDisposableContainerExtensions
    {
        public static void AddDisposable<T>(this T subscriptable, IDisposable disposable)
            where T : IDisposableContainer
        {
            if (subscriptable != null && subscriptable.Disposables != null)
                subscriptable.Disposables.Add(disposable);
        }

        public static void AddDisposable<T>(this T disposableContainer, Action onDispose)
            where T : IDisposableContainer
        {
            if (onDispose != null)
                disposableContainer.Disposables.Add(Disposable.Create(onDispose));
        }

        public static void DisposeSubscriptions<T>(this T subscriptable)
            where T : IDisposableContainer
        {
            if (subscriptable.Disposables != null)
                subscriptable.Disposables.ForEach(s => s.Dispose());
        }

    }

    public class DisposableContainer : IDisposableContainer
    {

        public DisposableContainer(Action onDispose = null, params IDisposable[] disposables)
        {
            if (onDispose != null)
                this.AddDisposable(onDispose);
            disposables.ForEach(d => this.AddDisposable(d));
        }

        public DisposableContainer(params Action[] onDispose)
        {
            onDispose.ForEach(od => this.AddDisposable(od));
        }

        public void Dispose()
        {
            this.DisposeSubscriptions();
        }

        public List<IDisposable> Disposables
        {
            get;
            private set;
        }
    }

    public class DelegateDisposable : IDisposable
    {
        public Action OnDispose { get; set; }

        public DelegateDisposable(Action onDispose = null)
        {
            if (onDispose != null)
                OnDispose = onDispose;
        }

        public void Dispose()
        {
            if (OnDispose != null)
                OnDispose();
        }
    }

    public static class Disposable
    {
        public static IDisposable AsDisposable(this Action onDispose)
        {
            return new DelegateDisposable(onDispose);
        }

        public static IDisposable Create(Action onDispose)
        {
            return new DelegateDisposable(onDispose);
        }

        public static IDisposable Create<T>(T reference, Action<T> onDispose)
            where T : class
        {
            WeakReference<T> weakReference = new WeakReference<T>(reference);
            return new DelegateDisposable(() =>
            {
                T target = null;
                if (weakReference.TryGetTarget(out target))
                    onDispose(target);
            });
        }

        public static DisposableContainer CreateContainer(params Action[] onDispose)
        {
            return new DisposableContainer(onDispose);
        }

        public static DisposableContainer CreateContainer(params IDisposable[] disposables)
        {
            return new DisposableContainer(null, disposables);
        }
    }

    #endregion
}

