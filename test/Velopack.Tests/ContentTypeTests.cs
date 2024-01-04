using System.Xml;
using Velopack.NuGet;

namespace Velopack.Tests
{
    public class ContentTypeTests
    {
        [Theory]
        [InlineData("basic.xml", "basic-merged.xml")]
        [InlineData("complex.xml", "complex-merged.xml")]
        public void MergeContentTypes(string inputFileName, string expectedFileName)
        {
            var inputFile = PathHelper.GetFixture("content-types", inputFileName);
            var expectedFile = PathHelper.GetFixture("content-types", expectedFileName);
            var tempFile = Path.GetTempFileName() + ".xml";

            var expected = new XmlDocument();
            expected.Load(expectedFile);

            var existingTypes = GetContentTypes(expected);

            try {
                File.Copy(inputFile, tempFile);

                var actual = new XmlDocument();
                actual.Load(tempFile);

                ContentType.Merge(actual);

                var actualTypes = GetContentTypes(actual);

                Assert.Equal(existingTypes, actualTypes);
            } finally {
                File.Delete(tempFile);
            }
        }

        static IEnumerable<XmlElement> GetContentTypes(XmlNode doc)
        {
            var expectedTypesElement = doc.FirstChild.NextSibling;
            return expectedTypesElement.ChildNodes.OfType<XmlElement>();
        }
    }
}
