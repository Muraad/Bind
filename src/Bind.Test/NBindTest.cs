using System;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Bind;

namespace Bind.Test
{
    public class BaseModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void SetPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
    public class Person : BaseModel
    {
        public int Age { get; set; }
        public string Name { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public Person()
        {
            Age = 42;
            Name = "Mustermann";
        }
    }

    public class Foo : BaseModel
    {
        int a;
        public int A
        {
            get { return a; }
            set
            {
                if (a == value)
                    return;
                a = value;
                SetPropertyChanged("A");
            }
        }


        int b;
        public int B
        {
            get { return b; }
            set
            {
                if (b == value)
                    return;
                b = value;
                SetPropertyChanged("B");
            }
        }
    }

    public class Bar
    {
        public int C { get; set; }
    }

    public class Adder
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }
    }

    [TestClass]
    public class NBindTest
    {
        void ComparePersons(Person p1, Person p2)
        {
            Assert.AreEqual(p1.Age, p2.Age);
            Assert.AreEqual(p1.Name, p2.Name);
        }

        [TestMethod]
        public void NBind_SimpleBindingTest()
        {
            var person1 = new Person();
            var person2 = new Person();

            var binding = NBind.Create(() => person1.Age == person2.Age && person1.Name == person2.Name);

            person2.Age = 43;
            person2.SetPropertyChanged("Age");
            person2.Name = "NewName";
            person2.SetPropertyChanged("Name");
            ComparePersons(person1, person2);

            person1.Age = 44;
            person1.SetPropertyChanged("Age");
            person1.Name = "NewName2";
            person1.SetPropertyChanged("Name");
            ComparePersons(person1, person2);
        }

        [TestMethod]
        public void NBind_ComplexBindingTest()
        {
            var person1 = new Person();
            var person2 = new Person();

            var binding = NBind.Create(() => person1.Age == person2.Age && person1.Name == person2.Name + " Hello World");

            person2.Age = 43;
            person2.SetPropertyChanged("Age");
            person2.Name = "NewName";
            person2.SetPropertyChanged("Name");
            Assert.AreEqual(person1.Name, person2.Name + " Hello World");
        }

        [TestMethod]
        public void NBind_ComplexBindingTest2()
        {
            var foo = new Foo() { A = 0, B = 0 };
            var bar = new Bar();

            var binding = NBind.Create(() => bar.C == Adder.Add(foo.A, foo.B));

            foo.A = 42;
            Assert.AreEqual(42, bar.C);
            foo.B = 42;
            Assert.AreEqual(84, bar.C);
        }
    }
}
