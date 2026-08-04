# Local AI — the no-cloud anchor

Snapture's AI surface is opt-in and local-only. Cloud LLM endpoints are explicitly rejected and are never presented as a provider choice.

## The rule

The allowed data path is:

`Snapture.exe → loopback runtime → local model`

The local runtime may be installed and managed separately by the user, but Snapture accepts only loopback HTTP/HTTPS endpoints. It does not accept an arbitrary URL, API key, cloud provider, or remote model endpoint.

## Supported providers

Settings → **AI tools** probes the following local runtimes. The tab is always available so a user can start a runtime and refresh without restarting Snapture.

| Provider | Discovery | OpenAI-compatible send endpoint | Provider prefix |
|----------|-----------|----------------------------------|-----------------|
| **Ollama** | `http://127.0.0.1:11434/api/tags` | `http://127.0.0.1:11434/v1/chat/completions` | `ollama/<model>` |
| **Foundry Local** | `foundry service status`, then the reported loopback `/openai/status` | the reported loopback `/v1/chat/completions` | `foundry/<model>` |
| **LM Studio** | `http://127.0.0.1:1234/v1/models` | `http://127.0.0.1:1234/v1/chat/completions` | `lmstudio/<model>` |

Foundry Local dynamically assigns its service port. Snapture parses the CLI output and validates the service's own endpoint list before using it. Non-loopback URLs are discarded.

## Send to local LLM

The editor's **Local AI** button:

1. Re-discovers available local models.
2. Shows only discovered local `provider/model` choices. Cloud providers are absent from the list.
3. Prefers `ollama/llava...` and `foundry/phi-3.5-vision...` when those models are available; otherwise the first discovered model is selected.
4. Flattens the current capture through the same export path used for PNG output, including annotations, adjustments, and frame wrappers.
5. Sends the flattened PNG as a `data:image/png;base64,...` image part in an OpenAI-compatible chat-completions request.
6. Displays the model response in a local result window.

The request is created only after the user chooses a model and clicks **Send PNG**. Snapture sends no `Authorization` header and does not store the image or response as an AI service transcript.

## What this does NOT enable

- No cloud LLM endpoints: OpenAI, Anthropic, Gemini, OpenRouter, Azure OpenAI, or any other external host.
- No "Ask Copilot" or "Analyze with AI" feature that routes through Microsoft's cloud.
- No model downloads from Snapture. Models are installed and managed by the selected local runtime.
- No telemetry, model usage analytics, prompt logging, or capture upload.

## Privacy claim chain

The local-AI contract fits the broader privacy audit:

- [What never happens](PRIVACY.md#what-never-happens) covers the no-launch/no-capture/no-save phone-home anchor.
- [Local AI providers](PRIVACY.md#local-ai-providers-loopback-only) documents the loopback probes and the user-triggered image request.
- [LAN share](PRIVACY.md#lan-share-server-off-by-default) is the separate opt-in path that can expose a file to another device on the user's network.
- [Manual update checks](PRIVACY.md#update-check-manual-only) are the only Snapture-owned external HTTP request.
- [Plugins](PRIVACY.md#plugins-third-party-code-optional) are third-party code and can leave the privacy boundary if the user installs one that declares or performs network access.

See [PRIVACY.md](PRIVACY.md) for the full network-call audit.
