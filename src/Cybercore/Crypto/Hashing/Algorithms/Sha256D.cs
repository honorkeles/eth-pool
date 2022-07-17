using System;
using System.Security.Cryptography;
using Cybercore.Contracts;

namespace Cybercore.Crypto.Hashing.Algorithms
{
    /// <summary>
    /// Sha-256 double round
    /// </summary>
    public class Sha256D : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            using (var hasher = SHA256.Create())
            {
                hasher.TryComputeHash(data, result, out _);
                hasher.TryComputeHash(result, result, out _);
            }
        }
    }
}