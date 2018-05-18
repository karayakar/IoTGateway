﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using Waher.Content;
using Waher.Security.JWS;

namespace Waher.Security.ACME
{
	/// <summary>
	/// Implements an ACME client for the generation of certificates using ACME-compliant certificate servers.
	/// </summary>
	public class AcmeClient : IDisposable
	{
		private readonly Uri directoryEndpoint;
		private HttpClient httpClient;
		private AcmeDirectory directory = null;
		private RsaSsaPkcsSha256 jws;
		private string nonce = null;

		/// <summary>
		/// Implements an ACME client for the generation of certificates using ACME-compliant certificate servers.
		/// </summary>
		/// <param name="DirectoryEndpoint">HTTP endpoint for the ACME directory resource.</param>
		public AcmeClient(string DirectoryEndpoint)
			: this(new Uri(DirectoryEndpoint))
		{
		}

		/// <summary>
		/// Implements an ACME client for the generation of certificates using ACME-compliant certificate servers.
		/// </summary>
		/// <param name="DirectoryEndpoint">HTTP endpoint for the ACME directory resource.</param>
		public AcmeClient(Uri DirectoryEndpoint)
		{
			this.directoryEndpoint = DirectoryEndpoint;
			this.jws = new RsaSsaPkcsSha256(4096, DirectoryEndpoint.ToString());

			this.httpClient = new HttpClient(new HttpClientHandler()
			{
				AllowAutoRedirect = true,
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
				CheckCertificateRevocationList = true,
				SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12
			}, true);

			this.httpClient.DefaultRequestHeaders.Add("User-Agent", typeof(AcmeClient).Namespace);
			this.httpClient.DefaultRequestHeaders.Add("Accept", JwsAlgorithm.JwsContentType);
			this.httpClient.DefaultRequestHeaders.Add("Accept-Language", "en");
		}

		/// <summary>
		/// <see cref="IDisposable.Dispose"/>
		/// </summary>
		public void Dispose()
		{
			if (this.httpClient != null)
			{
				this.httpClient.Dispose();
				this.httpClient = null;
			}

			if (this.jws != null)
			{
				this.jws.Dispose();
				this.jws = null;
			}
		}

		/// <summary>
		/// Gets the ACME directory.
		/// </summary>
		/// <returns>Directory object.</returns>
		public async Task<AcmeDirectory> GetDirectory()
		{
			if (this.directory == null)
				this.directory = new AcmeDirectory(this, await this.GET(this.directoryEndpoint));

			return this.directory;
		}

		internal async Task<IEnumerable<KeyValuePair<string, object>>> GET(Uri URL)
		{
			HttpResponseMessage Response = await this.httpClient.GetAsync(URL);

			Stream Stream = await Response.Content.ReadAsStreamAsync();
			byte[] Bin = await Response.Content.ReadAsByteArrayAsync();
			string CharSet = Response.Content.Headers.ContentType?.CharSet;
			Encoding Encoding;

			if (string.IsNullOrEmpty(CharSet))
				Encoding = Encoding.UTF8;
			else
				Encoding = System.Text.Encoding.GetEncoding(CharSet);

			string JsonResponse = Encoding.GetString(Bin);

			if (!(JSON.Parse(JsonResponse) is IEnumerable<KeyValuePair<string, object>> Obj))
				throw new Exception("Unexpected response returned.");

			if (Response.IsSuccessStatusCode)
				return Obj;
			else
				throw CreateException(Obj);
		}

		internal async Task<string> NextNonce()
		{
			if (!string.IsNullOrEmpty(this.nonce))
			{
				string s = this.nonce;
				this.nonce = null;
				return s;
			}

			if (this.directory == null)
				await this.GetDirectory();

			HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Head, this.directory.NewNonce);
			HttpResponseMessage Response = await this.httpClient.SendAsync(Request);
			Response.EnsureSuccessStatusCode();

			if (Response.Headers.TryGetValues("Replay-Nonce", out IEnumerable<string> Values))
			{
				foreach (string s in Values)
					return s;
			}

			throw new Exception("No nonce returned from server.");
		}

		/// <summary>
		/// Directory object.
		/// </summary>
		public Task<AcmeDirectory> Directory
		{
			get
			{
				if (this.directory == null)
					return this.GetDirectory();
				else
					return Task.FromResult<AcmeDirectory>(this.directory);
			}
		}

		/// <summary>
		/// Creates an account on the ACME server.
		/// </summary>
		/// <param name="ContactURLs">URLs for contacting the account holder.</param>
		/// <param name="TermsOfServiceAgreed">If the terms of service have been accepted.</param>
		/// <returns>ACME account object.</returns>
		public async Task<AcmeAccount> CreateAccount(string[] ContactURLs, bool TermsOfServiceAgreed)
		{
			if (this.directory == null)
				await this.GetDirectory();

			return new AcmeAccount(this, await this.POST(this.directory.NewAccount,
				new KeyValuePair<string, object>("termsOfServiceAgreed", TermsOfServiceAgreed),
				new KeyValuePair<string, object>("contact", ContactURLs)));
		}

		internal async Task<IEnumerable<KeyValuePair<string, object>>> POST(Uri URL, params KeyValuePair<string, object>[] Payload)
		{
			this.jws.Sign(new KeyValuePair<string, object>[]
			{
				new KeyValuePair<string, object>("nonce", await this.NextNonce()),
				new KeyValuePair<string, object>("url", URL.ToString())
			}, Payload, out string HeaderString, out string PayloadString, out string Signature);

			string Json = JSON.Encode(new KeyValuePair<string, object>[]
			{
				new KeyValuePair<string, object>("protected", HeaderString),
				new KeyValuePair<string, object>("payload", PayloadString),
				new KeyValuePair<string, object>("signature", Signature)
			}, null);

			HttpContent Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(Json));
			Content.Headers.Add("Content-Type", JwsAlgorithm.JwsContentType);
			HttpResponseMessage Response = await this.httpClient.PostAsync(URL, Content);

			this.GetNextNonce(Response);

			Stream Stream = await Response.Content.ReadAsStreamAsync();
			byte[] Bin = await Response.Content.ReadAsByteArrayAsync();
			string CharSet = Response.Content.Headers.ContentType?.CharSet;
			Encoding Encoding;

			if (string.IsNullOrEmpty(CharSet))
				Encoding = Encoding.UTF8;
			else
				Encoding = System.Text.Encoding.GetEncoding(CharSet);

			string JsonResponse = Encoding.GetString(Bin);

			if (!(JSON.Parse(JsonResponse) is IEnumerable<KeyValuePair<string, object>> Obj))
				throw new Exception("Unexpected response returned.");

			if (Response.IsSuccessStatusCode)
				return Obj;
			else
				throw CreateException(Obj);
		}

		internal static AcmeException CreateException(IEnumerable<KeyValuePair<string, object>> Obj)
		{
			AcmeException[] Subproblems = null;
			string Type = null;
			string Detail = null;
			int? Status = null;

			foreach (KeyValuePair<string, object> P in Obj)
			{
				switch (P.Key)
				{
					case "type":
						Type = P.Value as string;
						break;

					case "detail":
						Detail = P.Value as string;
						break;

					case "status":
						if (int.TryParse(P.Value as string, out int i))
							Status = i;
						break;

					case "subproblems":
						if (P.Value is Array A)
						{
							List<AcmeException> Subproblems2 = new List<AcmeException>();

							foreach (object Obj2 in A)
							{
								if (Obj2 is IEnumerable<KeyValuePair<string, object>> Obj3)
									Subproblems2.Add(CreateException(Obj3));
							}

							Subproblems = Subproblems2.ToArray();
						}
						break;
				}
			}

			if (Type.StartsWith("urn:ietf:params:acme:error:"))
			{
				switch (Type.Substring(27))
				{
					case "accountDoesNotExist": return new AcmeAccountDoesNotExistException(Type, Detail, Status, Subproblems);
					case "badCSR": return new AcmeBadCsrException(Type, Detail, Status, Subproblems);
					case "badNonce": return new AcmeBadNonceException(Type, Detail, Status, Subproblems);
					case "badRevocationReason": return new AcmeBadRevocationReasonException(Type, Detail, Status, Subproblems);
					case "badSignatureAlgorithm": return new AcmeBadSignatureAlgorithmException(Type, Detail, Status, Subproblems);
					case "caa": return new AcmeCaaException(Type, Detail, Status, Subproblems);
					case "compound": return new AcmeCompoundException(Type, Detail, Status, Subproblems);
					case "connection": return new AcmeConnectionException(Type, Detail, Status, Subproblems);
					case "dns": return new AcmeDnsException(Type, Detail, Status, Subproblems);
					case "externalAccountRequired": return new AcmeExternalAccountRequiredException(Type, Detail, Status, Subproblems);
					case "incorrectResponse": return new AcmeIncorrectResponseException(Type, Detail, Status, Subproblems);
					case "invalidContact": return new AcmeInvalidContactException(Type, Detail, Status, Subproblems);
					case "malformed": return new AcmeMalformedException(Type, Detail, Status, Subproblems);
					case "rateLimited": return new AcmeRateLimitedException(Type, Detail, Status, Subproblems);
					case "rejectedIdentifier": return new AcmeRejectedIdentifierException(Type, Detail, Status, Subproblems);
					case "serverInternal": return new AcmeServerInternalException(Type, Detail, Status, Subproblems);
					case "tls": return new AcmeTlsException(Type, Detail, Status, Subproblems);
					case "unauthorized": return new AcmeUnauthorizedException(Type, Detail, Status, Subproblems);
					case "unsupportedContact": return new AcmeUnsupportedContactException(Type, Detail, Status, Subproblems);
					case "unsupportedIdentifier": return new AcmeUnsupportedIdentifierException(Type, Detail, Status, Subproblems);
					case "userActionRequired": return new AcmeUserActionRequiredException(Type, Detail, Status, Subproblems);
					default: return new AcmeException(Type, Detail, Status, Subproblems);
				}
			}
			else
				return new AcmeException(Type, Detail, Status, Subproblems);
		}

		private void GetNextNonce(HttpResponseMessage Response)
		{
			if (Response.Headers.TryGetValues("Replay-Nonce", out IEnumerable<string> Values))
			{
				foreach (string s in Values)
				{
					this.nonce = s;
					return;
				}
			}
		}


	}
}