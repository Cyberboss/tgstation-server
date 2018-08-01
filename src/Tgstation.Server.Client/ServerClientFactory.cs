﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tgstation.Server.Client
{
	/// <inheritdoc />
	public sealed class ServerClientFactory : IServerClientFactory
	{
		/// <inheritdoc />
		public Task<IServerClient> CreateServerClient(string hostname, string username, string password, int timeout, CancellationToken cancellationToken) => throw new NotImplementedException();

		/// <inheritdoc />
		public Task<IServerClient> CreateServerClient(string hostname, string token, int timeout, CancellationToken cancellationToken) => throw new NotImplementedException();
	}
}