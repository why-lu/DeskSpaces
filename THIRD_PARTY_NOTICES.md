# 第三方说明

## 运行时与框架

DeskSpaces 使用 Microsoft .NET 8 / WPF / Windows Forms。.NET 是 Microsoft 的开源项目，采用 MIT 许可证；运行时许可证随对应 .NET 发行包提供。

## 参考实现

Windows 虚拟桌面私有 COM 接口的声明和校验思路参考：

- MScholtes/VirtualDesktop（MIT）：https://github.com/MScholtes/VirtualDesktop
- roost 的 26200 兼容性验证记录：https://github.com/WillyGarage/roost/blob/main/docs/NOTES.md

未复制其整个项目；DeskSpaces 只保留必要的接口声明与版本适配层。参考项目的权利仍归其各自作者。

## 不使用的组件

早期实验使用过 Ciantic/VirtualDesktopAccessor（MIT），因当前 Windows 26200 环境只读检测返回 -1，已从正式技术路线移除，不随发布包分发。

## CI 依赖

工作流使用 GitHub 官方 `actions/checkout`、`actions/setup-dotnet`、`actions/upload-artifact`，以及 SignPath 官方 `signpath/github-action-submit-signing-request`。这些依赖已固定到具体提交 SHA：checkout v7.0.1、setup-dotnet v6.1.0、upload-artifact v7.0.2、SignPath Action v3.2.0，并保留上游许可。
