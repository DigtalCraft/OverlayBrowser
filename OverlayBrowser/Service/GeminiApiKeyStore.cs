using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OverlayBrowser.Service;

/// <summary>
/// Gemini APIキーをWindows資格情報マネージャーへ保存する。
/// </summary>
public sealed class GeminiApiKeyStore
{
    private const string CredentialTarget = "OverlayBrowser/GeminiApiKey";
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int CredentialNotFound = 1168;

    /// <summary>
    /// 保存済みのGemini APIキーを取得する。
    /// </summary>
    /// <returns>APIキー。未登録の場合はnull。</returns>
    public string? GetApiKey()
    {
        if (!NativeMethods.CredRead(CredentialTarget, GenericCredentialType, 0, out var credentialPointer))
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == CredentialNotFound)
            {
                return null;
            }

            throw new Win32Exception(errorCode, "Windows資格情報マネージャーを読み込めませんでした。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            NativeMethods.CredFree(credentialPointer);
        }
    }

    /// <summary>
    /// Gemini APIキーが設定済みか確認する。
    /// </summary>
    /// <returns>設定済みの場合はtrue。</returns>
    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(GetApiKey());
    }

    /// <summary>
    /// Gemini APIキーを保存する。
    /// </summary>
    /// <param name="apiKey">保存するAPIキー。</param>
    /// <exception cref="ArgumentException">APIキーが空の場合に発生する。</exception>
    /// <exception cref="Win32Exception">資格情報マネージャーへの保存に失敗した場合に発生する。</exception>
    public void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("APIキーが空です。", nameof(apiKey));
        }

        var targetName = Marshal.StringToCoTaskMemUni(CredentialTarget);
        var userName = Marshal.StringToCoTaskMemUni("OverlayBrowser");
        var credentialBlob = Marshal.StringToCoTaskMemUni(apiKey.Trim());
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetName,
                CredentialBlob = credentialBlob,
                CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(apiKey.Trim()),
                Persist = LocalMachinePersistence,
                UserName = userName
            };

            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows資格情報マネージャーへ保存できませんでした。");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetName);
            Marshal.FreeCoTaskMem(userName);
            Marshal.FreeCoTaskMem(credentialBlob);
        }
    }

    /// <summary>
    /// 保存済みのGemini APIキーを削除する。
    /// </summary>
    /// <exception cref="Win32Exception">資格情報マネージャーから削除できない場合に発生する。</exception>
    public void DeleteApiKey()
    {
        if (NativeMethods.CredDelete(CredentialTarget, GenericCredentialType, 0))
        {
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        if (errorCode != CredentialNotFound)
        {
            throw new Win32Exception(errorCode, "Windows資格情報マネージャーから削除できませんでした。");
        }
    }

    /// <summary>
    /// Windows APIが使用する資格情報の構造体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        /// <summary>資格情報のフラグ。</summary>
        public uint Flags;
        /// <summary>資格情報の種類。</summary>
        public uint Type;
        /// <summary>資格情報を識別する名前。</summary>
        public IntPtr TargetName;
        /// <summary>資格情報に付随する説明。</summary>
        public IntPtr Comment;
        /// <summary>最終更新日時。</summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        /// <summary>資格情報本文のバイト数。</summary>
        public uint CredentialBlobSize;
        /// <summary>資格情報本文へのポインタ。</summary>
        public IntPtr CredentialBlob;
        /// <summary>資格情報の保存範囲。</summary>
        public uint Persist;
        /// <summary>追加属性の数。</summary>
        public uint AttributeCount;
        /// <summary>追加属性へのポインタ。</summary>
        public IntPtr Attributes;
        /// <summary>資格情報の別名。</summary>
        public IntPtr TargetAlias;
        /// <summary>資格情報のユーザー名。</summary>
        public IntPtr UserName;
    }

    /// <summary>
    /// Windows資格情報マネージャーAPIの宣言。
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// 保存済み資格情報を読み込む。
        /// </summary>
        /// <param name="targetName">読み込む資格情報の名前。</param>
        /// <param name="type">資格情報の種類。</param>
        /// <param name="flags">読み込みオプション。</param>
        /// <param name="credential">取得した資格情報へのポインタ。</param>
        /// <returns>読み込みに成功した場合はtrue。</returns>
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

        /// <summary>
        /// 資格情報を保存する。
        /// </summary>
        /// <param name="credential">保存する資格情報。</param>
        /// <param name="flags">保存オプション。</param>
        /// <returns>保存に成功した場合はtrue。</returns>
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWrite(ref NativeCredential credential, uint flags);

        /// <summary>
        /// 保存済み資格情報を削除する。
        /// </summary>
        /// <param name="targetName">削除する資格情報の名前。</param>
        /// <param name="type">資格情報の種類。</param>
        /// <param name="flags">削除オプション。</param>
        /// <returns>削除に成功した場合はtrue。</returns>
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDelete(string targetName, uint type, uint flags);

        /// <summary>
        /// Windows APIが確保した資格情報メモリを解放する。
        /// </summary>
        /// <param name="credential">解放する資格情報へのポインタ。</param>
        [DllImport("Advapi32.dll", SetLastError = true)]
        public static extern void CredFree(IntPtr credential);
    }
}
