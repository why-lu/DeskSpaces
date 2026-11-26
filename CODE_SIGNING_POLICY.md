# 代码签名政策

## 状态

DeskSpaces 计划申请 SignPath Foundation 的免费开源签名。**申请尚未获批，当前不得声称项目已有受信签名。**

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## 签名边界

- 发布二进制文件只能由 GitHub 托管的 Actions runner 从本仓库的指定提交自动构建。
- 不对本机、第三方或未追溯到本仓库提交的 EXE/DLL 请求 SignPath 签名。
- 签名应覆盖发布目录中的 DeskSpaces 自研 EXE/DLL 和所需测试程序；微软及 .NET 运行时已签名文件保持原签名，不重复签名。
- 每个版本使用相同的产品名称 `DeskSpaces`，版本号来自发布标签。
- 使用 SHA-256 文件摘要、受信 RFC 3161 时间戳和 SignPath 的制品配置。
- 签名通过后，下载 GitHub 发布制品，并在目标 Windows 版本实测启动和恢复功能后才发布。

## 角色

- **作者（Author）**：维护者可修改源码与构建脚本，并记录变更。成员：`why-lu`（仓库所有者）。
- **审查者（Reviewer）**：外部贡献必须通过 Pull Request 审查；维护者自己的变更保留可检查历史。成员：`why-lu`。
- **批准者（Approver）**：维护者审核构建来源、测试结果和发行说明后，批准每个签名请求。成员：`why-lu`（单人项目，作者与批准者暂为同一人；后续若有其他维护者将在此列明）。

## 凭据与访问

- SignPath API Token 仅存储在 GitHub Actions Secrets 中，绝不写入仓库、脚本、日志或聊天。
- 维护者的 GitHub 和 SignPath 账号必须启用多因素认证。
- 工作流只授予读取源码、读取 Actions 元数据和上传制品所需权限。
- 不使用自签名证书或修改系统信任设置绕过 Smart App Control。
- 隐私：本项目不会传输任何用户信息到未由用户指定的网络系统（详见仓库 [隐私说明](PRIVACY.md)）；第三方组件与服务的隐私政策见 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 撤销与变更

发现签名滥用、恶意改动或构建来源异常时，停止发布并联系 SignPath；撤销或更换受影响的发布制品。签名提供方、证书链或流程变化须同步修改本文件。
