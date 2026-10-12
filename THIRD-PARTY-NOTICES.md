# Third-party components

手残党界面独立实现，视觉参考 https://github.com/XeldarAlz/FFXIV-AutoFATEGrind，未复制该项目源码或资源。

| Component | Version | License / source |
|---|---|---|
| Dalamud / Dalamud.NET.Sdk | API 15 / SDK 15.0.0 | Runtime provided by launcher; https://github.com/goatcorp/Dalamud, SDK MIT |
| FFXIVClientStructs | bundled with Chinese Dalamud 15.0.3.6 | MIT; https://github.com/aers/FFXIVClientStructs |
| sherpa-onnx | 1.12.14 | Apache-2.0; https://github.com/k2-fsa/sherpa-onnx |
| ONNX Runtime | shipped by sherpa-onnx runtime 1.12.14 | MIT; https://github.com/microsoft/onnxruntime |
| NAudio.WinMM / Core | 2.2.1 | MIT; https://github.com/naudio/NAudio |
| Microsoft.Data.Sqlite | 10.0.0 | MIT; https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.11 | Apache-2.0; https://github.com/ericsink/SQLitePCL.raw |
| SQLite | SQLitePCLRaw native bundle | Public domain; https://sqlite.org/copyright.html |

Models are downloaded separately, never included in the plugin ZIP. Sources and model documentation:

- Chinese Zipformer 14M INT8: https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models
- CT-Transformer Chinese/English INT8 punctuation: https://github.com/k2-fsa/sherpa-onnx/releases/tag/punctuation-models
- Model usage/licensing information: https://k2-fsa.github.io/sherpa/onnx/pretrained_models/index.html

The sherpa-onnx repository's Apache license does not imply a blanket license for every third-party model or training corpus. Refer to the specific model's upstream documentation before redistribution. This project downloads the pinned upstream archives for local use.
