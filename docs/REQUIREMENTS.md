# 需求与兼容性

目标：Windows x64，国服 FF14 7.56，Dalamud API 15 / .NET 10。
已检查本机 Dalamud 15.0.3.6，官方 IChatGui 使用 IHandleableChatMessage 参数的新事件接口。
官方源码 https://github.com/goatcorp/Dalamud；游戏结构 https://github.com/aers/FFXIVClientStructs。
语音项目 https://github.com/k2-fsa/sherpa-onnx，模型与依赖版本固定。

按住录音、松开识别；小队/团队/喊话/说话/呼喊/部队/私聊；自动发送默认关闭。
超长按 UTF-8 字节和标点分段，自动队列间隔 2 秒，可取消；手动模式逐段填入。
留言保存收到的私聊、完整角色名/昵称提及及定向动作；按角色隔离。
不补取离线消息。轻量模型不保证识别所有语气，实际准确率需录音样本验收。
UI 参考 AutoFATEGrind 的视觉布局，独立编写，不复制其 AGPL 源码。

开发步骤分别提交：工程、聊天、语音、留言、集成验收。最终推送 origin/main。
