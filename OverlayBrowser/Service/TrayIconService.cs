using System.Drawing;
using System.Windows.Forms;

namespace OverlayBrowser.Service;

/// <summary>
/// タスクトレイのアイコンと操作メニューを管理する。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;

    /// <summary>
    /// タスクトレイから表示を選択した時に発生する。
    /// </summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// タスクトレイからヘルプを選択した時に発生する。
    /// </summary>
    public event EventHandler? HelpRequested;

    /// <summary>
    /// タスクトレイから終了を選択した時に発生する。
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// タスクトレイアイコンを初期化する。
    /// </summary>
    public TrayIconService()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("表示", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("ヘルプ / Help", null, (_, _) => HelpRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        notifyIcon = new NotifyIcon
        {
            Icon = CreateApplicationIcon(),
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
        notifyIcon.Dispose();
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

        return SystemIcons.Application;
    }
}
