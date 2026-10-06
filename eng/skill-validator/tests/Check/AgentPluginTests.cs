using System.Text.Json;
using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class AgentProfilerTests
{
    private static AgentInfo MakeAgent(
        string content,
        string name = "test-agent",
        string description = "Test agent description",
        string fileName = "test-agent.agent.md")
    {
        return new AgentInfo(name, description, $"/tmp/agents/{fileName}", content, fileName);
    }

    [TestClass]
    public class AgentDiscoveryPathSafetyTests
    {
        [TestMethod]
        public async Task PluginDiscoveryRejectsDeclaredAgentFileSymlink()
        {
            var root = Path.Combine(Path.GetTempPath(), $"agent-file-link-{Guid.NewGuid():N}");
            var pluginRoot = Path.Combine(root, "plugin");
            var agentsDir = Path.Combine(pluginRoot, "agents");
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(agentsDir);
            Directory.CreateDirectory(outsideDir);
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./agents/leak.agent.md"]
                }
                """);
            var outsideAgent = Path.Combine(outsideDir, "leak.agent.md");
            File.WriteAllText(outsideAgent, """
                ---
                name: leak
                description: External agent.
                ---
                External.
                """);
            if (!SymlinkTestHelper.TryCreateFile(Path.Combine(agentsDir, "leak.agent.md"), outsideAgent))
            {
                Directory.Delete(root, true);
                return;
            }
            try
            {
                Assert.IsEmpty(await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public async Task PluginDiscoveryRejectsDeclaredDirectorySymlink()
        {
            var root = Path.Combine(Path.GetTempPath(), $"agent-dir-link-{Guid.NewGuid():N}");
            var pluginRoot = Path.Combine(root, "plugin");
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(pluginRoot);
            Directory.CreateDirectory(outsideDir);
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./linked/"]
                }
                """);
            File.WriteAllText(Path.Combine(outsideDir, "outside.agent.md"), """
                ---
                name: outside
                description: External agent.
                ---
                External.
                """);
            if (!SymlinkTestHelper.TryCreateDirectory(Path.Combine(pluginRoot, "linked"), outsideDir))
            {
                Directory.Delete(root, true);
                return;
            }
            try
            {
                Assert.IsEmpty(await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public async Task PluginDiscoverySkipsLinkedAgentInsideConventionalDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), $"agent-mixed-link-{Guid.NewGuid():N}");
            var pluginRoot = Path.Combine(root, "plugin");
            var agentsDir = Path.Combine(pluginRoot, "agents");
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(agentsDir);
            Directory.CreateDirectory(outsideDir);
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./agents/"]
                }
                """);
            File.WriteAllText(Path.Combine(agentsDir, "real.agent.md"), """
                ---
                name: real
                description: Real agent.
                ---
                Real.
                """);
            var outsideAgent = Path.Combine(outsideDir, "linked.agent.md");
            File.WriteAllText(outsideAgent, """
                ---
                name: linked
                description: External agent.
                ---
                External.
                """);
            if (!SymlinkTestHelper.TryCreateFile(Path.Combine(agentsDir, "linked.agent.md"), outsideAgent))
            {
                Directory.Delete(root, true);
                return;
            }
            try
            {
                var agent = Assert.ContainsSingle(await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot));
                Assert.AreEqual("real", agent.Name);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ValidAgentProducesNoErrors()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test Agent\n\nDo the thing.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, "test-agent", "A test agent."));
        Assert.IsEmpty(profile.Errors);
    }

    [TestMethod]
    public void MissingFrontmatterErrors()
    {
        var content = "# Test Agent\n\nNo frontmatter here.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("frontmatter")));
    }

    [TestMethod]
    public void MissingFrontmatterUsesFilenameAsProfileName()
    {
        var content = "# Test Agent\n\nNo frontmatter here.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "", fileName: "my-agent.agent.md"));
        Assert.AreEqual("my-agent.agent.md", profile.Name);
    }

    [TestMethod]
    public void MissingNameErrors()
    {
        var content = "---\ndescription: A test agent.\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "", description: "A test agent."));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("name")));
    }

    [TestMethod]
    public void MissingDescriptionErrors()
    {
        var content = "---\nname: test-agent\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: ""));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("description")));
    }

    [TestMethod]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var content = $"---\nname: test-agent\ndescription: {desc}\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: desc));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("maximum")));
    }

    [TestMethod]
    public void DescriptionAtLimitNoError()
    {
        var desc = new string('a', 1024);
        var content = $"---\nname: test-agent\ndescription: {desc}\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: desc));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("maximum")));
    }

    [TestMethod]
    public void NameNotMatchingFilenameErrors()
    {
        var content = "---\nname: my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent", fileName: "different-agent.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("does not match filename")));
    }

    [TestMethod]
    public void NameMatchingFilenameNoError()
    {
        var content = "---\nname: my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent", fileName: "my-agent.agent.md"));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("does not match filename")));
    }

    [TestMethod]
    public async Task DiscoveryPreservesDeclaredAgentDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "parent.agent.md"), """
                ---
                name: parent
                description: Parent agent.
                agents:
                  - child-a
                  - child-b
                ---
                # Parent
                """);

            var agent = Assert.ContainsSingle(await AgentDiscovery.DiscoverAgentsInDirectory(root));

            Assert.AreSequenceEqual(["child-a", "child-b"], agent.Agents);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void NameWithUppercaseErrors()
    {
        var content = "---\nname: My-Agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "My-Agent", fileName: "My-Agent.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("invalid characters")));
    }

    [TestMethod]
    public void NameTooLongErrors()
    {
        var longName = new string('a', 65);
        var content = $"---\nname: {longName}\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: longName, fileName: $"{longName}.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("maximum is 64")));
    }

    [TestMethod]
    public void NameStartingWithHyphenErrors()
    {
        var content = "---\nname: -my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "-my-agent", fileName: "-my-agent.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("starts or ends with a hyphen")));
    }

    [TestMethod]
    public void NameEndingWithHyphenErrors()
    {
        var content = "---\nname: my-agent-\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent-", fileName: "my-agent-.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("starts or ends with a hyphen")));
    }

    [TestMethod]
    public void NameWithConsecutiveHyphensErrors()
    {
        var content = "---\nname: my--agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my--agent", fileName: "my--agent.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("consecutive hyphens")));
    }

    [TestMethod]
    public void ErrorMessagesSayAgentNotSkill()
    {
        var content = "---\nname: My-Agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "My-Agent", fileName: "My-Agent.agent.md"));
        Assert.IsTrue((profile.Errors).Any(e => e.StartsWith("Agent name")));
        Assert.IsFalse((profile.Errors).Any(e => e.StartsWith("Skill name")));
    }
}

[TestClass]
public class PluginProfilerTests
{
    [TestMethod]
    public void ValidPluginProducesNoErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], ["./agents/test.agent.md"], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsEmpty(result.Errors);
            Assert.IsEmpty(result.Warnings);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void MissingNameErrors()
    {
        var plugin = new PluginInfo("", "1.0.0", "desc", ["./skills/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("name")));
    }

    [TestMethod]
    public void NameNotMatchingDirectoryErrors()
    {
        var plugin = new PluginInfo("wrong-name", "1.0.0", "desc", ["./skills/"], [], "/tmp/my-plugin", "my-plugin");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("does not match directory")));
    }

    [TestMethod]
    public void MissingVersionErrors()
    {
        var plugin = new PluginInfo("test", null, "desc", ["./skills/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("version")));
    }

    [TestMethod]
    public void MissingDescriptionErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", null, ["./skills/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("description")));
    }

    [TestMethod]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var plugin = new PluginInfo("test", "1.0.0", desc, ["./skills/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("maximum")));
    }

    [TestMethod]
    public void MissingSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", [], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("skills")));
    }

    [TestMethod]
    public void NonexistentSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", ["./nonexistent/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("does not exist")));
    }

    [TestMethod]
    public void NonexistentAgentsPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./nonexistent.agent.md"], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("does not exist")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void NonexistentAgentFilePathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./agents/missing.agent.md"], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("does not exist")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void AgentDirectoryPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./agents/"], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("is a directory") && e.Contains("explicit file paths")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void ValidAgentPathsArrayProducesNoWarnings()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], ["./agents/test.agent.md"], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsEmpty(result.Errors);
            Assert.IsEmpty(result.Warnings);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("agents")]
    [DataRow("lspServers")]
    public void CodexManifestWithUnsupportedComponentErrors(string fieldName)
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            var dirName = Path.GetFileName(pluginDir);

            File.WriteAllText(
                Path.Combine(pluginDir, ".codex-plugin", "plugin.json"),
                $$"""{"name":"{{dirName}}","version":"1.0.0","description":"A test plugin.","skills":["./skills/"],"{{fieldName}}":[]}""");

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);

            Assert.IsTrue(result.Errors.Any(
                e => e.Contains(".codex-plugin/plugin.json") &&
                     e.Contains($"unsupported Codex field '{fieldName}'")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("name", "[]", "field 'name' must be string")]
    [DataRow("version", "{}", "field 'version' must be string")]
    [DataRow("description", "[]", "field 'description' must be string")]
    [DataRow("keywords", """["valid",1]""", "field 'keywords' must be an array of strings")]
    [DataRow("skills", "{}", "field 'skills' must be a string or an array of strings")]
    [DataRow("skills", "[]", "field 'skills' must contain at least one path")]
    [DataRow("skills", """["skills"]""", "field 'skills' path 'skills' must start with './'")]
    [DataRow("skills", """["./"]""", "field 'skills' path must not be './'")]
    [DataRow("skills", """["./packs/../packs/"]""", "field 'skills' path './packs/../packs/' must not contain '..'")]
    [DataRow("commands", "{}", "field 'commands' must be a string or an array of strings")]
    [DataRow("apps", "[]", "field 'apps' must be string")]
    [DataRow("hooks", "[true]", "field 'hooks' must be a string, object")]
    [DataRow("hooks", """["./hooks.json",{"hooks":{}}]""", "homogeneous array of strings or objects")]
    [DataRow("interface", "[]", "field 'interface' must be an object")]
    public void CodexManifestWithInvalidFieldShapeErrors(string fieldName, string invalidJson, string expectedError)
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            var dirName = Path.GetFileName(pluginDir);
            var properties = new Dictionary<string, string>
            {
                ["name"] = JsonSerializer.Serialize(dirName),
                ["version"] = "\"1.0.0\"",
                ["description"] = "\"A test plugin.\"",
                ["skills"] = """["./skills/"]""",
            };
            properties[fieldName] = invalidJson;
            var json = "{" + string.Join(",", properties.Select(p => $"\"{p.Key}\":{p.Value}")) + "}";
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), json);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);

            Assert.IsTrue((result.Errors).Any(error => error.Contains(expectedError)));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("name", "has no 'name' field")]
    [DataRow("skills", "has no 'skills' field")]
    public void CodexManifestMissingRequiredRepositoryFieldErrors(string omittedField, string expectedError)
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            var dirName = Path.GetFileName(pluginDir);
            var properties = new Dictionary<string, string>
            {
                ["name"] = JsonSerializer.Serialize(dirName),
                ["version"] = "\"1.0.0\"",
                ["description"] = "\"A test plugin.\"",
                ["skills"] = """["./skills/"]""",
            };
            properties.Remove(omittedField);
            var json = "{" + string.Join(",", properties.Select(p => $"\"{p.Key}\":{p.Value}")) + "}";
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), json);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);

            Assert.IsTrue((result.Errors).Any(error => error.Contains(expectedError)));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void ValidSkillPathsArrayProducesNoErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsEmpty(result.Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void NonexistentSkillPathInArrayErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./nonexistent/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("does not exist")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void NameFormatErrors()
    {
        var plugin = new PluginInfo("My_Plugin", "1.0.0", "desc", ["./skills/"], [], "/tmp/My_Plugin", "My_Plugin");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("invalid characters")));
    }

    [TestMethod]
    public void ErrorMessagesSayPluginNotSkill()
    {
        var plugin = new PluginInfo("My_Plugin", "1.0.0", "desc", ["./skills/"], [], "/tmp/My_Plugin", "My_Plugin");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.StartsWith("Plugin name")));
        Assert.IsFalse((result.Errors).Any(e => e.StartsWith("Skill name")));
    }

    [TestMethod]
    public void ParsePluginJsonReturnsNullForMissingFile()
    {
        var result = PluginDiscovery.ParsePluginJson("/nonexistent/plugin.json");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParsePluginJsonParsesValidFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":["./skills/"],"agents":["./agents/"]}""");

            var plugin = PluginDiscovery.ParsePluginJson(jsonPath);
            Assert.IsNotNull(plugin);
            Assert.AreEqual("my-plugin", plugin.Name);
            Assert.AreEqual("0.1.0", plugin.Version);
            Assert.AreEqual("A plugin.", plugin.Description);
            Assert.ContainsSingle(plugin.SkillPaths);
            Assert.AreEqual("./skills/", plugin.SkillPaths[0]);
            Assert.ContainsSingle(plugin.AgentPaths);
            Assert.AreEqual("./agents/", plugin.AgentPaths[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void ParsePluginJsonNormalizesStringToArray()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":"./skills/","agents":"./agents/"}""");

            var plugin = PluginDiscovery.ParsePluginJson(jsonPath);
            Assert.IsNotNull(plugin);
            Assert.ContainsSingle(plugin.SkillPaths);
            Assert.AreEqual("./skills/", plugin.SkillPaths[0]);
            Assert.ContainsSingle(plugin.AgentPaths);
            Assert.AreEqual("./agents/", plugin.AgentPaths[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void ParsePluginJsonNoAgentsField()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":"./skills/"}""");

            var plugin = PluginDiscovery.ParsePluginJson(jsonPath);
            Assert.IsNotNull(plugin);
            Assert.IsEmpty(plugin.AgentPaths);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void ParsePluginJsonThrowsOnMalformedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, "{ not valid json!!!");

            Assert.ThrowsExactly<JsonException>(() => PluginDiscovery.ParsePluginJson(jsonPath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // A non-object root would make TryGetProperty throw InvalidOperationException, which callers
    // catching JsonException would not handle.
    [TestMethod]
    [DataRow("[]", "array")]
    [DataRow("null", "null")]
    [DataRow("\"a string\"", "string")]
    [DataRow("42", "number")]
    public void ParsePluginJsonThrowsJsonExceptionOnNonObjectRoot(string json, string expectedKind)
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, json);

            var ex = Assert.ThrowsExactly<JsonException>(() => PluginDiscovery.ParsePluginJson(jsonPath));
            Assert.Contains($"root value is {expectedKind}", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void AbsoluteSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", ["/etc/skills/"], [], "/tmp/test", "test");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.IsTrue((result.Errors).Any(e => e.Contains("invalid") && e.Contains("absolute")));
    }

    [TestMethod]
    public void TraversalSkillsPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            var dirName = Path.GetFileName(pluginDir);
            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["../../../etc/"], [], pluginDir, dirName);
            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("invalid") && e.Contains("outside")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void MissingNameFallsBackToDirectoryName()
    {
        var plugin = new PluginInfo("", "1.0.0", "desc", ["./skills/"], [], "/tmp/my-plugin", "my-plugin");
        var result = PluginProfiler.ValidatePlugin(plugin);
        Assert.AreEqual("my-plugin", result.Name);
    }
}
