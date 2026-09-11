using System.Collections.Generic;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SerializableGuidTests
    {
        [Test]
        public void EmptyIsNotValid()
        {
            Assert.IsFalse(SerializableGuid.Empty.IsValid);
        }

        [Test]
        public void DefaultValueEqualsEmpty()
        {
            Assert.AreEqual(SerializableGuid.Empty, default(SerializableGuid));
        }

        [Test]
        public void NewGuidIsValid()
        {
            Assert.IsTrue(SerializableGuid.NewGuid().IsValid);
        }

        [Test]
        public void NewGuidProducesDistinctValues()
        {
            Assert.AreNotEqual(SerializableGuid.NewGuid(), SerializableGuid.NewGuid());
        }

        [Test]
        public void FromStringIsDeterministic()
        {
            Assert.AreEqual(SerializableGuid.FromString("line-1"), SerializableGuid.FromString("line-1"));
        }

        [Test]
        public void FromStringProducesDifferentValuesForDifferentSeeds()
        {
            Assert.AreNotEqual(SerializableGuid.FromString("line-1"), SerializableGuid.FromString("line-2"));
        }

        [Test]
        public void FromStringOfNullOrEmptyIsEmpty()
        {
            Assert.AreEqual(SerializableGuid.Empty, SerializableGuid.FromString(null));
            Assert.AreEqual(SerializableGuid.Empty, SerializableGuid.FromString(""));
        }

        [Test]
        public void EqualityOperatorsMatchEquals()
        {
            var a = SerializableGuid.FromString("seed");
            var b = SerializableGuid.FromString("seed");
            var c = SerializableGuid.NewGuid();

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
            Assert.IsFalse(a == c);
        }

        [Test]
        public void EqualValuesHaveEqualHashCodes()
        {
            var a = SerializableGuid.FromString("seed");
            var b = SerializableGuid.FromString("seed");

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void WorksAsADictionaryKey()
        {
            var dictionary = new Dictionary<SerializableGuid, string>();
            var key = SerializableGuid.FromString("node-1");
            dictionary[key] = "value";

            Assert.IsTrue(dictionary.TryGetValue(SerializableGuid.FromString("node-1"), out var value));
            Assert.AreEqual("value", value);
        }

        [Test]
        public void ToStringOfEmptyIsEmptyString()
        {
            Assert.AreEqual(string.Empty, SerializableGuid.Empty.ToString());
        }

        [Test]
        public void ToStringOfAValidGuidIsNotEmpty()
        {
            Assert.AreNotEqual(string.Empty, SerializableGuid.NewGuid().ToString());
        }

        [Test]
        public void FromGuidRoundTripsExactly()
        {
            var guid = System.Guid.NewGuid();
            var wrapped = SerializableGuid.FromGuid(guid);

            Assert.AreEqual(guid, wrapped.ToGuid());
        }
    }
}
