using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeskSpaces;

// 参考 MScholtes/VirtualDesktop（MIT）24H2 接口布局，以及 roost 的 26200 验证记录。
// 私有接口只封装在适配层；GetCount 与注册表交叉验证后才允许调用其他方法。
internal static class NativeDesktopSwitch
{
    public static void SwitchTo(Guid targetId, int registryCount)
    {
        var manager = CreateManager();
        var count = manager.GetCount();
        if (count != registryCount || registryCount < 1)
            throw new InvalidOperationException("虚拟桌面接口校验失败，已取消切换。");
        var target = manager.FindDesktop(ref targetId);
        if (target is null || target.GetId() != targetId)
            throw new InvalidOperationException("目标虚拟桌面不存在或已改变。");
        manager.SwitchDesktop(target);
        Marshal.FinalReleaseComObject(target);
        Marshal.FinalReleaseComObject(manager);
    }

    private static IVirtualDesktopManagerInternal CreateManager()
    {
        var shellType = Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"))
            ?? throw new InvalidOperationException("无法定位 Windows 虚拟桌面服务。");
        var shell = (IServiceProvider10)Activator.CreateInstance(shellType)!;
        var service = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
        var iid = typeof(IVirtualDesktopManagerInternal).GUID;
        try { return (IVirtualDesktopManagerInternal)shell.QueryService(ref service, ref iid); }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    private interface IObjectArray
    {
        void GetCount(out int count);
        void GetAt(int index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
    private interface IVirtualDesktop
    {
        void Unused1();
        Guid GetId();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("53F5CA0B-158F-4124-900C-057158060B27")]
    private interface IVirtualDesktopManagerInternal
    {
        int GetCount();
        void MoveViewToDesktop(object view, IVirtualDesktop desktop);
        bool CanViewMoveDesktops(object view);
        IVirtualDesktop GetCurrentDesktop();
        void GetDesktops(out IObjectArray desktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop from, int direction, out IVirtualDesktop desktop);
        void SwitchDesktop(IVirtualDesktop desktop);
        void SwitchDesktopAndMoveForegroundView(IVirtualDesktop desktop);
        IVirtualDesktop CreateDesktop();
        void MoveDesktop(IVirtualDesktop desktop, int index);
        void RemoveDesktop(IVirtualDesktop desktop, IVirtualDesktop fallback);
        IVirtualDesktop FindDesktop(ref Guid id);
        void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktop desktop, out IObjectArray first, out IObjectArray second);
        void SetDesktopName(IVirtualDesktop desktop, IntPtr name);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IServiceProvider10
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid iid);
    }
}
