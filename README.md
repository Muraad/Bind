# Based on Praeclarum.Bind 

See https://github.com/praeclarum/Bind for full explanation.
This version is a little bit cleaned up. There was a code path not necessary. A binding is now an IDisposable
that can be used to unbind.

```c#
    public class Person : INotifyPropertyChanged
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

        public event PropertyChangedEventHandler PropertyChanged;

        public void SetPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propName));
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

            var binding = NBind.Bind(() => person1.Age == person2.Age && person1.Name == person2.Name);

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

            var binding = NBind.Bind(() => person1.Age == person2.Age && person1.Name == person2.Name + " Hello World");

            person2.Age = 43;
            person2.SetPropertyChanged("Age");
            person2.Name = "NewName";
            person2.SetPropertyChanged("Name");
            Assert.AreEqual(person1.Name, person2.Name + " Hello World");


        }
    }
```
