using System.Linq;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptJsonNodesRuntimeTests {
        [Fact]
        public void JsonNodeTypesHaveDedicatedRuntimeImportDefinitions() {
            var definitions = TypeScriptRuntimeRequirementCatalog.BaseRequirements;
            Assert.Contains(definitions, entry => entry.Name == "JsonNode" && entry.Path == "./system/text/json/nodes/json-node");
            Assert.Contains(definitions, entry => entry.Name == "JsonObject" && entry.Path == "./system/text/json/nodes/json-object");
            Assert.Contains(definitions, entry => entry.Name == "JsonArray" && entry.Path == "./system/text/json/nodes/json-array");
            Assert.Contains(definitions, entry => entry.Name == "JsonValue" && entry.Path == "./system/text/json/nodes/json-value");
        }
    }
}