using NUnit.Framework;
using System;

namespace Praeclarum.Bind.Test
{
	[TestFixture]
	public class EqualityInitTest
	{
		[SetUp]
		public void SetUp ()
		{
            Bind.Error += message => Console.WriteLine(message);
		}

		[Test]
		public void LocalLeftInit ()
		{
			var left = "";
			var right = "hello";

			Bind.Create (() => left == right);

			Assert.AreEqual (left, right);
			Assert.AreEqual (left, "hello");
		}

		[Test]
		public void LocalRightInit ()
		{
			var left = "hello";
			var right = "";

            Bind.Create(() => left == right);

			Assert.AreEqual (left, right);
			Assert.AreEqual (left, "");
		}

		class TestObject
		{
			public int State { get; set; }
		}

		[Test]
		public void LocalLeftObjectInit ()
		{
			TestObject left = null;
			TestObject right = new TestObject ();

            Bind.Create(() => left == right);

			Assert.AreSame (left, right);
			Assert.IsNotNull (left);
		}

		[Test]
		public void LocalRightObjectInit ()
		{
			TestObject left = new TestObject ();
			TestObject right = null;

            Bind.Create(() => left == right);

			Assert.AreSame (left, right);
			Assert.IsNull (left);
		}

		[Test]
		public void LocalAndPropInit ()
		{
			var left = 69;
			TestObject right = new TestObject {
				State = 42,
			};

            Bind.Create(() => left == right.State);

			Assert.AreEqual (left, right.State);
			Assert.AreEqual (left, 42);
		}

		[Test]
		public void PropAndLocalInit ()
		{
			TestObject left = new TestObject {
				State = 42,
			};
			var right = 1001;

            Bind.Create(() => left.State == right);

			Assert.AreEqual (left.State, right);
			Assert.AreEqual (left.State, 1001);
		}

		static int Method() { return 33; }

		[Test]
		public void LocalAndMethodInit ()
		{
			var left = 0;

            Bind.Create(() => left == Method());

			Assert.AreEqual (left, 33);
		}

		[Test]
		public void MethodAndLocalInit ()
		{
			var right = 42;

			Bind.Create (() => Method () == right);

			Assert.AreEqual (right, 33);
		}
	}
}

























