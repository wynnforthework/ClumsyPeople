# ClumsyPeople

**手残党**：国服 FF14 的本地离线语音输入与留言板插件。

目标环境：**Windows x64、国服 7.56、Dalamud API 15、.NET 10**。本地构建验证使用国服 Dalamud **15.0.3.6**。当前为开发预览版，游戏内验收待完成。

## 功能

- 按住频道按钮说话，松开后识别中文并恢复标点；麦克风、模型路径和词汇纠正可配置。
- 小队 `/p`、团队 `/a`、喊话 `/sh`、说话 `/s`、呼喊 `/y`、部队 `/fc`、私聊 `/tell`。
- 默认填入游戏聊天框，例如 `/p 你好`。勾选“识别后直接发送”后自动提交。
- 超长文本按 UTF-8 字节及标点分段；计入频道命令和私聊收件人。自动发送间隔 2 秒，可取消；手动模式逐段填入。
- 收集收到的私聊、角色名/昵称提及及明确指向自己的动作，跨重启保存。按角色隔离，支持搜索、已读、删除和 JSON 导出。
- `/clumsy` 打开主窗口，支持紧凑频道按钮窗口。

录音仅保存在内存，识别不上传音频。模型首次下载需联网。不能补取角色离线期间未接收到的消息。

## 安装与模型

1. 从构建产物解压 `ClumsyPeople-0.1.0.0.zip` 到独立目录，保持 `runtimes/win-x64/native` 目录结构。
2. 在 Dalamud `/xlsettings` 的开发插件位置添加解压后的 `ClumsyPeople.dll`，加载后输入 `/clumsy`。
3. 在“设置”选择麦克风，点击“下载并校验模型”。如需代理，在下载代理中填 `http://127.0.0.1:端口`。两个模型压缩包共约 **133 MiB**，安装后约 150 MiB；默认位置为插件配置目录的 `models`。
4. 本地导入时，将下列两个目录放在同一个根目录，设置该根目录路径：
   - `sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23`
   - `sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8`
5. 首次识别需要加载模型，会比后续识别慢。语音最长 60 秒；失焦或关闭窗口会取消录音。较短语音、嘈杂环境、游戏术语可能需要手动修正。

也可运行 `./scripts/install-models.ps1 -Destination '你的模型目录'` 安装。文件版本和 SHA256 固定；下载或校验失败不会静默换用其他模型。

私聊填写“角色名@服务器”，例如 `测试玩家@红玉海`。昵称匹配为包含匹配，昵称过短可能收录无关聊天。已有草稿会阻止填入或自动发送。

## 开发

安装 .NET **10.0.100 或兼容的 .NET 10 SDK**，指定国服 Dalamud 15 程序集目录：

```powershell
./scripts/build.ps1 -DalamudPath "$env:APPDATA\XIVLauncherCN\addon\Hooks\26-10-08-02"
```

脚本依次运行锁定依赖还原、Release 构建（警告视为错误）、测试和打包，产物位于 `.artifacts/ClumsyPeople-0.1.0.0.zip`。
附加 `-Models '模型根目录'` 可以运行真实 ASR/标点原生推理测试。普通测试无需麦克风或模型。
测试项目是可执行断言测试器，失败返回非零退出码；使用 `dotnet run --project ClumsyPeople.Tests -c Release`。

CI 固定国服 Dalamud 分发仓库提交，不下载国际服浮动最新版。手动 Release 工作流生成草稿，不自动公开发布。

开发环境中若 Windows Schannel 报 `SEC_E_NO_CREDENTIALS`，Git 可单次使用 `git -c http.sslBackend=openssl ...`，配合已有 HTTP 代理；不要关闭证书校验。

## 验收与限制

见 [实机验收清单](docs/ACCEPTANCE.md)、[验证报告](docs/VALIDATION.md)和 [30 条语音样例](docs/SPEECH-CORPUS.md)。
本地自动测试不等同于游戏内验收，尤其是聊天框焦点、国服字节边界、真实发送及动作目标判定，需要在国服客户端验证。

插件限制整条聊天命令不超过 500 字节，并取游戏输入框报告的更小上限。若游戏对特定频道施加额外限制，会停止后续自动分段；已提交消息不自动重试。
“已提交”代表调用客户端接口，不代表服务器确认送达。精确问号/感叹号和普通话字符错误率尚需真实语音样例测量。

第三方信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
