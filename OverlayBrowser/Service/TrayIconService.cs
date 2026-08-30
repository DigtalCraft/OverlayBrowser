using System.Drawing;
using System.Windows.Forms;

namespace OverlayBrowser.Service;

/// <summary>
/// タスクトレイのアイコンと操作メニューを管理する。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ContextMenuStrip contextMenu;
    private readonly Icon trayIcon;
    private readonly NotifyIcon notifyIcon;

    /// <summary>
    /// タスクトレイから表示を選択した時に発生する。
    /// </summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// タスクトレイから終了を選択した時に発生する。
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// タスクトレイアイコンを初期化する。
    /// </summary>
    public TrayIconService()
    {
        trayIcon = CreateApplicationIcon();
        contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = true
        };

        var openMenuItem = new ToolStripMenuItem("開く", trayIcon.ToBitmap());
        openMenuItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var exitMenuItem = new ToolStripMenuItem("終了", SystemIcons.Error.ToBitmap());
        exitMenuItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        contextMenu.Items.Add(openMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitMenuItem);

        notifyIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "Overlay Browser",
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// タスクトレイアイコンを破棄する。
    /// </summary>
    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip = null;
        notifyIcon.Dispose();
        contextMenu.Dispose();
        trayIcon.Dispose();
    }

    /// <summary>
    /// 実行ファイルのアイコンをタスクトレイ用のアイコンとして取得する。
    /// </summary>
    /// <returns>タスクトレイへ表示するアイコン。</returns>
    private static Icon CreateApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
