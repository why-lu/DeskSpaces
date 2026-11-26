# DeskSpaces

适用于 Windows 11 25H2（26200）x64 的原生虚拟桌面图标工作区。**开发预览，默认不接管桌面。**

## 目标与当前状态

目标是在 Windows 原生虚拟桌面上增加独立图标层：每个桌面拥有自己的图标集合和自由坐标，切换系统桌面时同时切换窗口和图标布局。

- 已实现入口布局、自由拖动、归一化坐标、复制/移动入口、布局备份和恢复守护。
- 核心自动测试 9/9 通过；独立系统原型已验证嵌入、原生桌面往返切换和异常恢复。
- **正式 EXE 的集成验收尚未完成，不能把原型结果当作正式版验收。**
- 目前使用 SignPath Foundation 开源签名路线；申请与受信构建尚待完成，详见 [代码签名政策](CODE_SIGNING_POLICY.md)。

## 安全原则

- 默认不移动、重命名或删除真实桌面文件。
- 不修改注册表，不替换或重启 Explorer，不设置开机启动。
- 每个原生虚拟桌面有独立入口布局和归一化坐标。
- 跨桌面复制/移动默认只处理入口，不复制真实文件。
- 布局采用临时文件原子替换，保留最多 10 份备份。
- 不收集或上传桌面文件、路径、布局、截图或个人数据；详见 [隐私说明](PRIVACY.md)。

## 使用与恢复

当前预览依赖 .NET 8 Windows Desktop Runtime。启动 `DeskSpaces.exe` 后，默认打开预览窗口，不会隐藏系统图标。

- 拖动图标后保存位置，重启软件保持布局。
- 切换条对应 Windows 原生桌面。
- 右键可复制入口；剪切/复制后切换到目标桌面粘贴，仅改变入口，不移动实体文件。
- 接管模式只应在明确确认后开启，失败应自动恢复原图标。

退出：选择切换条的“退出”，或托盘的“恢复原桌面并退出”。无法操作软件时运行程序目录中的 `restore-desktop.cmd`。

**卸载：**先退出并确认原图标恢复，再备份需要的 `Data` 布局，最后移除程序目录。不会留下驱动、启动项或注册表信任修改。详见 [安全说明](SECURITY.md)。

## 构建

需要 .NET 8 SDK；GitHub Actions 自动安装。项目本地开发可以使用 `.dotnet` 便携 SDK，但它不会进入 Git 仓库。

```powershell
# 使用项目脚本，临时文件与 NuGet 缓存均位于项目目录。
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target test
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target publish

# 需要自包含包时：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target publish -SelfContained
```

当前应用无第三方 NuGet 依赖；`NuGet.Config` 仅配置微软官方 NuGet 源，供自包含发布下载 .NET 运行时。若本机网络不能访问 NuGet，普通构建和核心测试仍可执行，自包含发布交由 GitHub 的 Windows runner 验证。脚本不会修改系统 PATH、执行策略或注册表。

## 开源签名

[MIT 许可证](LICENSE) · [签名政策](CODE_SIGNING_POLICY.md) · [第三方说明](THIRD_PARTY_NOTICES.md)

发布版应从 GitHub 托管 runner 对指定源码提交构建，并通过 SignPath 校验仓库、提交和工作流来源。不能把本机编译、来源不明的 EXE/DLL 拿去申请签名。

API Token 只能由维护者添加到 GitHub Actions Secrets。未配置真实 SignPath 项目参数时，CI 只生成未签名预览制品。

## 技术边界

Windows 完整的虚拟桌面管理接口并非稳定公开 API。软件将私有 COM 调用隔离在适配层，先校验接口返回的桌面数量和桌面标识，失败时停止而不是盲试。

已废弃 VirtualDesktopAccessor 实验，不随发布包分发。当前使用只读的虚拟桌面标识跟随系统切换，创建和删除桌面使用 Windows 官方键盘快捷键。
