using System;
using System.Security.Cryptography;
using System.Text;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class CredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AiDataGateway.LocalLogViewer.DatabaseProfile.v1");

        public string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var clearBytes = Encoding.UTF8.GetBytes(plainText);
            try
            {
                return Convert.ToBase64String(ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser));
            }
            finally
            {
                Array.Clear(clearBytes, 0, clearBytes.Length);
            }
        }

        public string Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText)) return string.Empty;
            var protectedBytes = Convert.FromBase64String(protectedText);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(clearBytes);
            }
            finally
            {
                Array.Clear(clearBytes, 0, clearBytes.Length);
                Array.Clear(protectedBytes, 0, protectedBytes.Length);
            }
        }
    }
}
