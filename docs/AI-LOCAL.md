# Local AI — the no-cloud anchor

Snapture supports local AI providers as an opt-in feature. Cloud LLM endpoints are explicitly rejected.

## The rule

If the byte path is `Snapture.exe → localhost`, it's allowed. If it's `Snapture.exe → external host`, it's not.

## Supported providers (planned, v0.8.6)

| Provider | Discovery | Model examples |
|----------|-----------|----------------|
| **Ollama** | Probes `http://localhost:11434/api/tags` | LLaVA, Phi-3.5-vision |
| **Foundry Local** | Probes Windows AI Foundry runtime | Phi-3.5-vision ONNX |
| **LM Studio** | Probes `http://localhost:1234/v1/models` | Any local GGUF model |

Provider syntax follows Peekaboo v3.2.0: `ollama/<model>` or `lmstudio/<model>`.

## What this enables

- "Send to local LLM" button in the editor sends the flattened capture as base64 PNG to the chosen local model.
- Auto-redact can use RapidOCR's bundled DBNet text-region detector (one ONNX model serves both OCR and redaction).
- Settings → AI tools tab appears only when a local provider is detected on localhost.

## What this does NOT enable

- No cloud LLM endpoints: OpenAI, Anthropic, Gemini, OpenRouter, Azure OpenAI — all absent from the dropdown.
- No "Ask Copilot" or "Analyze with AI" features that route through Microsoft's cloud.
- No model downloads that phone home to a CDN (models are managed by Ollama / Foundry / LM Studio, not Snapture).

## Why

The Lightshot scandal showed what happens when a screenshot tool's data path includes a cloud server. Snapture's privacy anchor is simple: your pixels never leave your machine unless you explicitly share them. Local LLM providers honor this because the model runs on your hardware.

See also: [PRIVACY.md](PRIVACY.md) for the full network-call audit.
