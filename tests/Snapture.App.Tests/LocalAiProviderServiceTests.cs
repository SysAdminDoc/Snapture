using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class LocalAiProviderServiceTests
{
    [TestMethod]
    public void OllamaPayloadParsesNamesAndDeduplicatesModels()
    {
        var models = LocalAiProviderService.ParseOllamaModels("""
            {"models":[
              {"name":"llava:latest","model":"llava:latest"},
              {"name":"llava:latest"},
              {"name":"qwen2.5vl:7b"}
            ]}
            """);

        CollectionAssert.AreEqual(
            new[] { "llava:latest", "qwen2.5vl:7b" },
            models.Select(model => model.Id).ToArray());
    }

    [TestMethod]
    public void OpenAiPayloadUsesDataIdsAndDisplayNames()
    {
        var models = LocalAiProviderService.ParseOpenAiModels("""
            {"data":[
              {"id":"phi-3.5-vision","owned_by":"local"},
              {"id":"qwen2-vl","display_name":"Qwen 2 VL"}
            ]}
            """);

        Assert.AreEqual("phi-3.5-vision", models[0].Id);
        Assert.AreEqual("phi-3.5-vision", models[0].Label);
        Assert.AreEqual("Qwen 2 VL", models[1].Label);
    }

    [TestMethod]
    public void FoundryPayloadSupportsCachedModelArrayAndCatalogObjects()
    {
        var cached = LocalAiProviderService.ParseFoundryModels(
            "[\"Phi-4-mini-instruct-generic-cpu\",\"phi-3.5-mini\"]");
        var catalog = LocalAiProviderService.ParseFoundryModels(
            "{\"models\":[{\"name\":\"phi-3.5-vision\",\"displayName\":\"Phi-3.5 Vision\"}]} ");

        CollectionAssert.AreEqual(
            new[] { "Phi-4-mini-instruct-generic-cpu", "phi-3.5-mini" },
            cached.Select(model => model.Id).ToArray());
        Assert.AreEqual("Phi-3.5 Vision", catalog[0].Label);
    }

    [TestMethod]
    public void FoundryStatusOnlyAcceptsLoopbackEndpoints()
    {
        var endpoints = LocalAiProviderService.ParseFoundryStatusEndpoints("""
            {"Endpoints":[
              "http://localhost:5272",
              "http://127.0.0.1:5273/v1",
              "https://example.invalid:5274",
              "file://C:/models"
            ]}
            """);

        CollectionAssert.AreEqual(
            new[] { "http://localhost:5272/", "http://127.0.0.1:5273/" },
            endpoints.Select(endpoint => endpoint.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public void CliOutputOnlyExtractsLoopbackHttpUrls()
    {
        var endpoints = LocalAiProviderService.ParseLocalEndpoints(
            "running at http://127.0.0.1:54210 and https://localhost:54211; cloud https://models.example.invalid");

        CollectionAssert.AreEqual(
            new[] { "http://127.0.0.1:54210/", "https://localhost:54211/" },
            endpoints.Select(endpoint => endpoint.AbsoluteUri).ToArray());
        Assert.IsFalse(LocalAiProviderService.IsLoopbackHttpUri(new Uri("https://models.example.invalid")));
    }

    [TestMethod]
    public void ProviderReferencesUseTheDocumentedProviderPrefix()
    {
        var provider = new LocalAiProviderInfo(
            LocalAiProviderKind.Ollama,
            LocalAiProviderService.OllamaKey,
            "Ollama",
            new Uri("http://127.0.0.1:11434/v1/"),
            true,
            new[] { new LocalAiModel("llava:latest") },
            "Detected · 1 model");

        Assert.AreEqual(
            "ollama/llava:latest",
            LocalAiProviderService.FormatModelReference(provider, provider.Models[0]));
    }

    [TestMethod]
    public void PreferredModelsMatchTheRoadmapDefaults()
    {
        var ollama = new LocalAiProviderInfo(
            LocalAiProviderKind.Ollama,
            LocalAiProviderService.OllamaKey,
            "Ollama",
            new Uri("http://127.0.0.1:11434/v1/"),
            true,
            new[] { new LocalAiModel("qwen2.5vl:7b"), new LocalAiModel("llava:latest") },
            "Detected · 2 models");
        var foundry = ollama with
        {
            Kind = LocalAiProviderKind.FoundryLocal,
            Key = LocalAiProviderService.FoundryKey,
            DisplayName = "Foundry Local",
            Models = new[] { new LocalAiModel("phi-4-mini"), new LocalAiModel("phi-3.5-vision-instruct-generic-cpu") }
        };

        Assert.AreEqual("llava:latest", LocalAiProviderService.FindPreferredModel(ollama)?.Id);
        Assert.AreEqual("phi-3.5-vision-instruct-generic-cpu", LocalAiProviderService.FindPreferredModel(foundry)?.Id);
    }
}
