using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GalgameUiTranslator
{
    public static class ApiCredentialStore
    {
        private const uint CredentialTypeGeneric = 1;
        private const uint CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const string TargetPrefix = "GalgameUiTranslator/API/";

        public static string GetCredentialId(string baseUrl, string model)
        {
            switch (DetectCredentialProvider(baseUrl, model))
            {
                case ApiProviderKind.Gemini:
                    return "gemini";
                case ApiProviderKind.DeepSeek:
                    return "deepseek";
                default:
                    return "custom-" + HashCustomEndpoint(baseUrl, model);
            }
        }

        public static string GetProviderLabel(string baseUrl, string model)
        {
            switch (DetectCredentialProvider(baseUrl, model))
            {
                case ApiProviderKind.Gemini:
                    return "Gemini";
                case ApiProviderKind.DeepSeek:
                    return "DeepSeek";
                default:
                    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                        return uri.Host;
                    return "自定义接口";
            }
        }

        private static ApiProviderKind? DetectCredentialProvider(string baseUrl, string model)
        {
            var value = (baseUrl ?? string.Empty).Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                if (host.Equals("googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase))
                    return ApiProviderKind.Gemini;
                if (host.Equals("deepseek.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".deepseek.com", StringComparison.OrdinalIgnoreCase))
                    return ApiProviderKind.DeepSeek;
                return null;
            }

            return value.Length == 0 ? ApiProviderProfiles.Detect(value, model) : (ApiProviderKind?)null;
        }

        public static bool TryRead(string baseUrl, string model, out string apiKey)
        {
            apiKey = string.Empty;
            if (!OperatingSystem.IsWindows()) return false;
            var target = TargetPrefix + GetCredentialId(baseUrl, model);
            if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPointer))
            {
                return false;
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return false;
                if (credential.CredentialBlobSize > int.MaxValue) return false;
                var bytes = new byte[(int)credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                try
                {
                    apiKey = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                    return !string.IsNullOrWhiteSpace(apiKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }

        public static void Write(string baseUrl, string model, string apiKey)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("API 密钥安全存储仅支持 Windows。");
            var value = (apiKey ?? string.Empty).Trim();
            if (value.Length == 0) return;
            var bytes = Encoding.Unicode.GetBytes(value);
            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = TargetPrefix + GetCredentialId(baseUrl, model),
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredentialPersistLocalMachine,
                    UserName = GetProviderLabel(baseUrl, model) + " API Key"
                };
                if (!CredWrite(ref credential, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入 Windows 凭据管理器。");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                for (var index = 0; index < value.Length * 2; index++) Marshal.WriteByte(blob, index, 0);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        public static void Delete(string baseUrl, string model)
        {
            if (!OperatingSystem.IsWindows()) return;
            var target = TargetPrefix + GetCredentialId(baseUrl, model);
            if (!CredDelete(target, CredentialTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound)
                    throw new Win32Exception(error, "无法从 Windows 凭据管理器删除 API 密钥。");
            }
        }

        private static string HashCustomEndpoint(string baseUrl, string model)
        {
            var identity = (baseUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
            if (identity.Length == 0) identity = (model ?? string.Empty).Trim().ToLowerInvariant();
            var bytes = Encoding.UTF8.GetBytes(identity);
            try
            {
                var hash = SHA256.HashData(bytes);
                return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(
            string target,
            uint type,
            uint reservedFlag,
            out IntPtr credentialPointer);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credentialPointer);
    }
}
