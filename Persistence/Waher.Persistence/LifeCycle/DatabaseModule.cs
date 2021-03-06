﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Waher.Runtime.Inventory;

namespace Waher.Persistence.LifeCycle
{
	/// <summary>
	/// Database module.
	/// </summary>
	public class DatabaseModule : IModule
	{
		/// <summary>
		/// Database module.
		/// </summary>
		public DatabaseModule()
		{
		}

		/// <summary>
		/// Starts the module.
		/// </summary>
		/// <returns>If an asynchronous start operation has been started, a wait handle is returned. This
		/// wait handle can be used to wait for the asynchronous process to finish. If no such asynchronous
		/// operation has been started, null can be returned.</returns>
		public WaitHandle Start()
		{
			return ToWaitHandle(Database.Provider.Start());
		}

		private static WaitHandle ToWaitHandle(Task T)
		{
			if (T is null)
				return null;

			ManualResetEvent Done = new ManualResetEvent(false);
			Task T2 = Wait(T, Done);

			return Done;
		}

		private static async Task Wait(Task T, ManualResetEvent Done)
		{
			try
			{
				await T;
			}
			finally
			{
				Done.Set();
			}
		}

		/// <summary>
		/// Stops the module.
		/// </summary>
		public void Stop()
		{
			Database.Provider.Stop()?.Wait();
		}

		/// <summary>
		/// Flushes any remaining data to disk.
		/// </summary>
		public static Task Flush()
		{
			return Database.Provider.Flush();
		}

	}
}
