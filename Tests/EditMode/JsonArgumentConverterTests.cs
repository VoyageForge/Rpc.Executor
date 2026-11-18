using System;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>默认参数转换器测试：覆盖基础类型、枚举、Guid、TimeSpan、null 与失败路径。</summary>
    public sealed class JsonArgumentConverterTests
    {
        private enum TestMode
        {
            Off,
            On
        }

        private JsonArgumentConverter _converter;

        [SetUp]
        public void SetUp()
        {
            _converter = new JsonArgumentConverter();
        }

        [Test]
        public void 字符串转int()
        {
            bool ok = _converter.TryConvert("42", typeof(int), out object converted, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(42, converted);
        }

        [Test]
        public void 已经是目标类型直接返回()
        {
            object value = 7;

            bool ok = _converter.TryConvert(value, typeof(int), out object converted, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreSame(value, converted);
        }

        [Test]
        public void 字符串转枚举忽略大小写()
        {
            bool ok = _converter.TryConvert("on", typeof(TestMode), out object converted, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(TestMode.On, converted);
        }

        [Test]
        public void 字符串转Guid()
        {
            Guid id = Guid.NewGuid();

            bool ok = _converter.TryConvert(id.ToString(), typeof(Guid), out object converted, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(id, converted);
        }

        [Test]
        public void 字符串转TimeSpan()
        {
            bool ok = _converter.TryConvert("00:01:30", typeof(TimeSpan), out object converted, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(TimeSpan.FromSeconds(90), converted);
        }

        [Test]
        public void Null允许赋给引用类型和Nullable()
        {
            Assert.IsTrue(_converter.TryConvert(null, typeof(string), out object referenceValue, out _));
            Assert.IsNull(referenceValue);

            Assert.IsTrue(_converter.TryConvert(null, typeof(int?), out object nullableValue, out _));
            Assert.IsNull(nullableValue);
        }

        [Test]
        public void Null不能赋给值类型()
        {
            bool ok = _converter.TryConvert(null, typeof(int), out _, out string error);

            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
        }

        [Test]
        public void 非法字符串转换失败并返回错误()
        {
            bool ok = _converter.TryConvert("not-a-number", typeof(int), out object converted, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(converted);
            Assert.IsNotNull(error);
        }
    }
}
