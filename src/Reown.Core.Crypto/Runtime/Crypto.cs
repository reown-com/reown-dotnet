﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Reown.Core.Common.Utils;
using Reown.Core.Crypto.Encoder;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Crypto.Models;
using Reown.Core.Network;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using ArgumentException = System.ArgumentException;
using ChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;

namespace Reown.Core.Crypto
{
    /// <summary>
    ///     The crypto module handles storing key pairs in storage. The storage module to use
    ///     must be given to the crypto module instance
    /// </summary>
    public class Crypto : ICrypto
    {
        private const string CryptoClientSeed = "client_ed25519_seed";
        private const string MulticodecEd25519Base = "z";
        private const string MulticodecEd25519Header = "K36";
        private const string DidDelimiter = ":";
        private const string DidPrefix = "did";
        private const string DidMethod = "key";
        private const long CryptoJwtTtl = Clock.ONE_DAY;
        private const string JwtDelimiter = ".";

        public const int Type0 = 0;
        public const int Type1 = 1;
        private const int TypeLength = 1;
        private const int IvLength = 12;
        private const int KeyLength = 32;
        private static readonly Encoding DataEncoding = Encoding.UTF8;
        private static readonly Encoding JsonEncoding = Encoding.UTF8;
        private readonly bool _newStorage;

        private bool _initialized;
        protected bool Disposed;

        /// <summary>
        ///     Create a new instance of the crypto module, with a given storage module.
        /// </summary>
        /// <param name="storage">The storage module to use to load the keychain from</param>
        public Crypto(IKeyValueStorage storage)
        {
            if (storage == null)
                throw new ArgumentException("storage must be non-null");

            KeyChain = new KeyChain(storage);
            Storage = storage;
        }

        /// <summary>
        ///     Create a new instance of the crypto module, with a given keychain.
        /// </summary>
        /// <param name="keyChain">The keychain to use for this crypto module</param>
        public Crypto(IKeyChain keyChain)
        {
            KeyChain = keyChain ?? throw new ArgumentException("keyChain must be non-null");
            Storage = keyChain.Storage;
        }

        /// <summary>
        ///     Create a new instance of the crypto module using an empty keychain stored in-memory using a Dictionary
        /// </summary>
        public Crypto() : this(new InMemoryStorage())
        {
            _newStorage = true;
        }

        /// <summary>
        ///     The current storage module this crypto module instance is using
        /// </summary>
        public IKeyValueStorage Storage { get; }

        /// <summary>
        ///     The name of the crypto module
        /// </summary>
        public string Name
        {
            get => "crypto";
        }

        /// <summary>
        ///     The current context of this module instance
        /// </summary>
        public string Context
        {
            get =>
                //TODO Replace with logger context
                "reown.core.crypto";
        }

        /// <summary>
        ///     The current KeyChain this crypto module instance is using
        /// </summary>
        public IKeyChain KeyChain { get; }

        /// <summary>
        ///     Hash a hex key string using SHA256. The input key string must be a hex
        ///     string and the returned hash is represented as a hex string
        /// </summary>
        /// <param name="key">The input hex key string to hash using SHA256</param>
        /// <returns>The hash of the given input as a hex string</returns>
        public string HashKey(string key)
        {
#if NET7_0_OR_GREATER
            return SHA256.HashData(key.HexToByteArray()).ToHex();
#else
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(key.HexToByteArray()).ToHex();
#endif
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Initialize the crypto module, this does nothing if the module has already
        ///     been initialized
        ///     Initializing the module will invoke Init() on the backing KeyChain
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                if (_newStorage)
                    await Storage.Init();

                await KeyChain.Init();
                _initialized = true;
            }
        }

        /// <summary>
        ///     Check if a keypair with a given tag is stored in this crypto module. This should
        ///     check the backing keychain.
        /// </summary>
        /// <param name="tag">The tag of the keychain to look for</param>
        /// <returns>True if the backing KeyChain has a keypair for the given tag</returns>
        public Task<bool> HasKeys(string tag)
        {
            IsInitialized();
            return KeyChain.Has(tag);
        }

        /// <summary>
        ///     Generate a new keypair, storing the public/private key pair as the tag in the backing KeyChain. This will
        ///     save the public/private keypair in the backing KeyChain
        /// </summary>
        /// <returns>The public key of the generated keypair</returns>
        public Task<string> GenerateKeyPair()
        {
            IsInitialized();

            // strength is not used so set to 1
            var options = new KeyGenerationParameters(SecureRandom.GetInstance("SHA256PRNG"), 1);
            var generator = new X25519KeyPairGenerator();
            generator.Init(options);

            var keypair = generator.GenerateKeyPair();

            if (keypair.Public is not X25519PublicKeyParameters publicKeyData)
            {
                throw new InvalidCastException($"Public key is not an {nameof(X25519PublicKeyParameters)}");
            }

            if (keypair.Private is not X25519PrivateKeyParameters privateKeyData)
            {
                throw new InvalidCastException($"Private key is not an {nameof(X25519PrivateKeyParameters)}");
            }

            var publicKey = publicKeyData.GetEncoded().ToHex();
            var privateKey = privateKeyData.GetEncoded().ToHex();

            return SetPrivateKey(publicKey, privateKey);
        }

        /// <summary>
        ///     Generate a shared Sym key given two public keys. One of the public keys (selfPublicKey) is the public key
        ///     we have generated a private key for in the backing KeyChain. The peer's public key (peerPublicKey) is used
        ///     to generate the Sym key
        /// </summary>
        /// <param name="selfPublicKey">The public key to use, this keypair must be stored in the backing KeyChain</param>
        /// <param name="peerPublicKey">The Peer's public key. This public key does not exist in the backing KeyChain</param>
        /// <param name="overrideTopic"></param>
        /// <returns>The generated Sym key</returns>
        public async Task<string> GenerateSharedKey(string selfPublicKey, string peerPublicKey,
            string overrideTopic = null)
        {
            var privateKey = await GetPrivateKey(selfPublicKey);
            var sharedKey = DeriveSharedKey(privateKey, peerPublicKey);
            var symKeyRaw = DeriveSymmetricKey(sharedKey);

            return await SetSymKey(symKeyRaw.ToHex(), overrideTopic);
        }

        /// <summary>
        ///     Store the Sym key in the backing KeyChain, optionally for a given topic. If no topic is given,
        ///     then the KeyChain tag for the Sym key will be the hash of the key.
        /// </summary>
        /// <param name="symKey">The Sym key to store</param>
        /// <param name="overrideTopic">An optional topic to use as the KeyChain tag</param>
        /// <returns>The tag used to store the Sym key in the KeyChain</returns>
        public async Task<string> SetSymKey(string symKey, string overrideTopic = null)
        {
            var topic = overrideTopic ?? HashKey(symKey);
            await KeyChain.Set(topic, symKey);

            return topic;
        }

        /// <summary>
        ///     Delete a keypair from the backing KeyChain
        /// </summary>
        /// <param name="publicKey">The public key of the keypair to delete</param>
        /// <returns>An async task</returns>
        public Task DeleteKeyPair(string publicKey)
        {
            IsInitialized();
            return KeyChain.Delete(publicKey);
        }

        /// <summary>
        ///     Delete a Sym key with the given topic/tag from the backing KeyChain.
        /// </summary>
        /// <param name="topic">The topic/tag of the Sym key to delete</param>
        /// <returns>An async task</returns>
        public Task DeleteSymKey(string topic)
        {
            IsInitialized();
            return KeyChain.Delete(topic);
        }

        /// <summary>
        ///     Encrypt a message with the given topic's Sym key.
        /// </summary>
        /// <param name="@params">The parameters that define what to encrypt and how</param>
        /// <returns>The encrypted message</returns>
        public string Encrypt(EncryptParams @params)
        {
            IsInitialized();

            var typeRaw = Bases.Base10.Decode($"{@params.Type}");
            var iv = @params.Iv;

            byte[] rawIv;
            if (iv == null)
            {
                rawIv = new byte[12];
                RandomNumberGenerator.Fill(rawIv);
            }
            else
            {
                rawIv = iv.HexToByteArray();
            }

            var type1 = @params.Type == Type1;
            var senderPublicKey = !string.IsNullOrWhiteSpace(@params.SenderPublicKey)
                ? @params.SenderPublicKey.HexToByteArray()
                : null;

            var aead = new ChaCha20Poly1305();
            aead.Init(true, new ParametersWithIV(new KeyParameter(@params.SymKey.HexToByteArray()), rawIv));

            var encoded = Encoding.UTF8.GetBytes(@params.Message);

            byte[] encrypted;
            using (var encryptedStream = new MemoryStream())
            {
                var temp = new byte[encoded.Length * 3];
                var len = aead.ProcessBytes(encoded, 0, encoded.Length, temp, 0);

                if (len > 0)
                {
                    encryptedStream.Write(temp, 0, len);
                }

                len = aead.DoFinal(temp, 0);
                if (len > 0)
                {
                    encryptedStream.Write(temp, 0, len);
                }

                encrypted = encryptedStream.ToArray();
            }

            if (type1)
            {
                if (senderPublicKey == null)
                    throw new ArgumentException("Missing sender public key for type1 envelope");

                return Convert.ToBase64String(
                    typeRaw.Concat(senderPublicKey).Concat(rawIv).Concat(encrypted).ToArray()
                );
            }

            return Convert.ToBase64String(
                typeRaw.Concat(rawIv).Concat(encrypted).ToArray()
            );
        }

        /// <summary>
        ///     Decrypt an encrypted message using the given topic's Sym key.
        /// </summary>
        /// <param name="topic">The topic of the Sym key to use to decrypt the message</param>
        /// <param name="encoded">The message to decrypt</param>
        /// <returns>The decrypted message from an async task</returns>
        public async Task<string> Decrypt(string topic, string encoded)
        {
            IsInitialized();
            var symKey = await GetSymKey(topic);

            return DeserializeAndDecrypt(symKey, encoded);
        }

        /// <summary>
        ///     Encode a JsonRpcPayload message by encrypting the contents using the given topic's Sym key. If the topic
        ///     has no Sym key, then the contents are not encrypted and instead are simply converted to Json -> Hex
        /// </summary>
        /// <param name="topic">The topic of the Sym key to use to encrypt the IJsonRpcPayload</param>
        /// <param name="payload">The payload to encode and encrypt</param>
        /// <param name="options">(optional) Encoding options</param>
        /// <returns>The encoded and encrypted IJsonRpcPayload from an async task</returns>
        public async Task<string> Encode(string topic, IJsonRpcPayload payload, EncodeOptions options = null)
        {
            IsInitialized();

            var validatedOptions = ValidateEncoding(options);
            var isTypeOne = IsTypeOneEnvelope(validatedOptions);

            if (isTypeOne && options != null)
            {
                var selfPublicKey = options.SenderPublicKey;
                var peerPublicKey = options.ReceiverPublicKey;
                topic = await GenerateSharedKey(selfPublicKey, peerPublicKey);
            }

            var symKey = await GetSymKey(topic);
            var type = validatedOptions.Type;
            var senderPublicKey = validatedOptions.SenderPublicKey;
            var message = JsonConvert.SerializeObject(payload);
            var results = Encrypt(new EncryptParams
            {
                Message = message,
                Type = type,
                SenderPublicKey = senderPublicKey,
                SymKey = symKey
            });

            return results;
        }

        /// <summary>
        ///     Decode an encoded/encrypted message to a IJsonRpcPayload using the given topic's Sym key. If the topic
        ///     has no Sym key, then the contents are not decrypted and instead are simply converted Hex -> Json
        /// </summary>
        /// <param name="topic">The topic of the Sym key to use</param>
        /// <param name="encoded">The encoded/encrypted message to decrypt</param>
        /// <param name="options">(optional) Decoding options</param>
        /// <typeparam name="T">The type of the IJsonRpcPayload to convert the encoded Json to</typeparam>
        /// <returns>The decoded, decrypted and deserialized object of type T from an async task</returns>
        public async Task<T> Decode<T>(string topic, string encoded, DecodeOptions options = null)
            where T : IJsonRpcPayload
        {
            IsInitialized();
            var @params = ValidateDecoding(encoded, options);
            var isType1 = IsTypeOneEnvelope(@params);

            if (isType1)
            {
                var selfPublicKey = @params.ReceiverPublicKey;
                var peerPublicKey = @params.SenderPublicKey;
                topic = await GenerateSharedKey(selfPublicKey, peerPublicKey);
            }

            var message = await Decrypt(topic, encoded);
            var payload = JsonConvert.DeserializeObject<T>(message);

            return payload;
        }

        /// <summary>
        ///     Given an aud value, create and sign a JWT token
        /// </summary>
        /// <param name="aud">The aud value to use</param>
        /// <returns>A signed JWT token represented as a string</returns>
        public async Task<string> SignJwt(string aud)
        {
            IsInitialized();
            var seed = await GetClientSeed();
            var keyPair = KeypairFromSeed(seed);
            var subRaw = new byte[32];
            RandomNumberGenerator.Fill(subRaw);
            var sub = subRaw.ToHex();
            var ttl = CryptoJwtTtl;
            var iat = Clock.Now();

            // sign JWT
            var header = IridiumJWTHeader.DEFAULT;
            var iss = EncodeIss(keyPair.GeneratePublicKey());
            var exp = iat + ttl;
            var payload = new IridiumJWTPayload
            {
                Iss = iss,
                Sub = sub,
                Aud = aud,
                Iat = iat,
                Exp = exp
            };

            var data = DataEncoding.GetBytes(
                string.Join(JwtDelimiter, EncodeJson(header), EncodeJson(payload))
            );

            var signer = new Ed25519Signer();
            signer.Init(true, keyPair);
            signer.BlockUpdate(data, 0, data.Length);

            var signature = signer.GenerateSignature();
            return EncodeJwt(new IridiumJWTSigned
            {
                Header = header,
                Payload = payload,
                Signature = signature
            });
        }

        /// <summary>
        ///     Get a unique client id for this client
        /// </summary>
        /// <returns>The client id as a string</returns>
        public async Task<string> GetClientId()
        {
            IsInitialized();
            var seed = await GetClientSeed();
            var keyPair = KeypairFromSeed(seed);
            var clientId = EncodeIss(keyPair.GeneratePublicKey());
            return clientId;
        }

        private static EncodingValidation ValidateEncoding(EncodeOptions options)
        {
            var type = options?.Type ?? Type0;
            if (type == Type1)
            {
                if (options == null || string.IsNullOrWhiteSpace(options.SenderPublicKey))
                {
                    throw new ArgumentException("Missing sender public key");
                }

                if (options == null || string.IsNullOrWhiteSpace(options.ReceiverPublicKey))
                {
                    throw new ArgumentException("Missing receiver public key");
                }
            }

            return new EncodingValidation
            {
                Type = type,
                ReceiverPublicKey = options?.ReceiverPublicKey,
                SenderPublicKey = options?.SenderPublicKey
            };
        }

        private EncodingValidation ValidateDecoding(string encoded, DecodeOptions options)
        {
            var deserialized = Deserialize(encoded);
            return ValidateEncoding(new EncodeOptions
            {
                Type = int.Parse(Bases.Base10.Encode(deserialized.Type)),
                SenderPublicKey = deserialized.SenderPublicKey?.ToHex(),
                ReceiverPublicKey = options?.ReceiverPublicKey
            });
        }

        private static bool IsTypeOneEnvelope(EncodingValidation param)
        {
            return param.Type == Type1
                   && !string.IsNullOrWhiteSpace(param.SenderPublicKey)
                   && !string.IsNullOrWhiteSpace(param.ReceiverPublicKey);
        }

        private EncodingParams Deserialize(string encoded)
        {
            var bytes = Convert.FromBase64String(encoded);
            var typeRaw = bytes.Take(TypeLength).ToArray();
            var slice1 = TypeLength;

            var type = int.Parse(Bases.Base10.Encode(typeRaw));
            if (type == Type1)
            {
                var slice2 = slice1 + KeyLength;
                var slice3 = slice2 + IvLength;
                var senderPublicKey = new ArraySegment<byte>(bytes, slice1, KeyLength);
                var iv = new ArraySegment<byte>(bytes, slice2, IvLength);
                var @sealed =
                    new ArraySegment<byte>(bytes, slice3, bytes.Length - (TypeLength + KeyLength + IvLength));

                return new EncodingParams
                {
                    Iv = iv.ToArray(),
                    Sealed = @sealed.ToArray(),
                    SenderPublicKey = senderPublicKey.ToArray(),
                    Type = typeRaw
                };
            }
            else
            {
                var slice2 = slice1 + IvLength;
                var iv = new ArraySegment<byte>(bytes, slice1, IvLength);
                var @sealed = new ArraySegment<byte>(bytes, slice2, bytes.Length - (IvLength + TypeLength));

                return new EncodingParams
                {
                    Type = typeRaw,
                    Sealed = @sealed.ToArray(),
                    Iv = iv.ToArray()
                };
            }
        }

        private static string EncodeJwt(IridiumJWTSigned data)
        {
            return string.Join(JwtDelimiter,
                EncodeJson(data.Header),
                EncodeJson(data.Payload),
                EncodeSig(data.Signature)
            );
        }

        private static string EncodeSig(byte[] signature)
        {
            return Base64.EncodeToBase64UrlString(signature);
        }

        private static string EncodeJson<T>(T data)
        {
            return Base64.EncodeToBase64UrlString(
                JsonEncoding.GetBytes(
                    JsonConvert.SerializeObject(data)
                )
            );
        }

        private static string EncodeIss(Ed25519PublicKeyParameters publicKey)
        {
            var publicKeyRaw = publicKey.GetEncoded();
            var header = Base58Encoding.Decode(MulticodecEd25519Header);
            var multicodec = MulticodecEd25519Base + Base58Encoding.Encode(header.Concat(publicKeyRaw).ToArray());

            return string.Join(DidDelimiter, DidPrefix, DidMethod, multicodec);
        }

        private static Ed25519PrivateKeyParameters KeypairFromSeed(byte[] seed)
        {
            return new Ed25519PrivateKeyParameters(seed);
        }

        private async Task<string> SetPrivateKey(string publicKey, string privateKey)
        {
            await KeyChain.Set(publicKey, privateKey);

            return publicKey;
        }

        private Task<string> GetPrivateKey(string publicKey)
        {
            return KeyChain.Get(publicKey);
        }

        private Task<string> GetSymKey(string topic)
        {
            return KeyChain.Get(topic);
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Crypto)} module not initialized.");
            }
        }

        private static byte[] DeriveSharedKey(string privateKeyA, string publicKeyB)
        {
            var keyA = new X25519PrivateKeyParameters(privateKeyA.HexToByteArray());
            var keyB = new X25519PublicKeyParameters(publicKeyB.HexToByteArray());
            var agreement = new X25519Agreement();
            agreement.Init(keyA);

            var data = new byte[agreement.AgreementSize];
            agreement.CalculateAgreement(keyB, data, 0);

            return data;
        }

        private static byte[] DeriveSymmetricKey(byte[] secretKey)
        {
            var generator = new HkdfBytesGenerator(new Sha256Digest());
            generator.Init(new HkdfParameters(secretKey, Array.Empty<byte>(), Array.Empty<byte>()));

            var key = new byte[32];
            generator.GenerateBytes(key, 0, 32);

            return key;
        }

        private string DeserializeAndDecrypt(string symKey, string encoded)
        {
            var param = Deserialize(encoded);
            var @sealed = param.Sealed;
            var iv = param.Iv;

            var aead = new ChaCha20Poly1305();
            aead.Init(false, new ParametersWithIV(new KeyParameter(symKey.HexToByteArray()), iv));

            using var rawDecrypted = new MemoryStream();
            var temp = new byte[@sealed.Length];
            var len = aead.ProcessBytes(@sealed, 0, @sealed.Length, temp, 0);

            if (len > 0)
            {
                rawDecrypted.Write(temp, 0, len);
            }

            len = aead.DoFinal(temp, 0);

            if (len > 0)
            {
                rawDecrypted.Write(temp, 0, len);
            }

            return Encoding.UTF8.GetString(rawDecrypted.ToArray());
        }

        private async Task<byte[]> GetClientSeed()
        {
            string seed;
            try
            {
                seed = await KeyChain.Get(CryptoClientSeed);
            }
            catch (InvalidOperationException)
            {
                var seedRaw = new byte[32];
                RandomNumberGenerator.Fill(seedRaw);
                seed = seedRaw.ToHex();
                await KeyChain.Set(CryptoClientSeed, seed);
            }

            return seed.HexToByteArray();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed)
                return;

            if (disposing)
            {
                KeyChain?.Dispose();
            }

            Disposed = true;
        }
    }
}