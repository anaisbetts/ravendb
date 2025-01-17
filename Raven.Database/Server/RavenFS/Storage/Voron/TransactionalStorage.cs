﻿// -----------------------------------------------------------------------
//  <copyright file="TransactionalStorage.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.Util.Streams;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.Server.RavenFS.Storage.Voron.Impl;
using Raven.Database.Server.RavenFS.Storage.Voron.Schema;

using Voron;
using Voron.Impl;
using Constants = Raven.Abstractions.Data.Constants;
using VoronExceptions = Voron.Exceptions;

namespace Raven.Database.Server.RavenFS.Storage.Voron
{
    public class TransactionalStorage : ITransactionalStorage
    {
	    private readonly InMemoryRavenConfiguration configuration;

	    private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        private readonly string path;

        private readonly NameValueCollection settings;

        private readonly ThreadLocal<IStorageActionsAccessor> current = new ThreadLocal<IStorageActionsAccessor>();

        private volatile bool disposed;

        private readonly ReaderWriterLockSlim disposerLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        private readonly BufferPool bufferPool;

        private TableStorage tableStorage;

        private IdGenerator idGenerator;

        public TransactionalStorage(InMemoryRavenConfiguration configuration)
        {
	        this.configuration = configuration;
	        path = configuration.FileSystemDataDirectory.ToFullPath();
	        settings = configuration.Settings;

            bufferPool = new BufferPool(2L * 1024 * 1024 * 1024, int.MaxValue); // 2GB max buffer size (voron limit)
        }

        public void Dispose()
        {
            disposerLock.EnterWriteLock();
            try
            {
                if (disposed)
                    return;
                disposed = true;
                current.Dispose();

                if (tableStorage != null)
                    tableStorage.Dispose();

                if (bufferPool != null)
                    bufferPool.Dispose();
            }
            finally
            {
                disposerLock.ExitWriteLock();
            }
        }

        public Guid Id { get; private set; }

        private static StorageEnvironmentOptions CreateStorageOptionsFromConfiguration(string path, NameValueCollection settings)
        {
            bool allowIncrementalBackupsSetting;
            if (bool.TryParse(settings["Raven/Voron/AllowIncrementalBackups"] ?? "false", out allowIncrementalBackupsSetting) == false)
                throw new ArgumentException("Raven/Voron/AllowIncrementalBackups settings key contains invalid value");

            var directoryPath = path ?? AppDomain.CurrentDomain.BaseDirectory;
            var filePathFolder = new DirectoryInfo(directoryPath);
            if (filePathFolder.Exists == false)
                filePathFolder.Create();

            var tempPath = settings["Raven/Voron/TempPath"];
            var journalPath = settings[Constants.RavenTxJournalPath];
            var options = StorageEnvironmentOptions.ForPath(directoryPath, tempPath, journalPath);
            options.IncrementalBackupEnabled = allowIncrementalBackupsSetting;
            return options;
        }

        public void Initialize()
        {
            bool runInMemory;
            bool.TryParse(settings["Raven/RunInMemory"], out runInMemory);

            var persistenceSource = runInMemory ? StorageEnvironmentOptions.CreateMemoryOnly() :
                CreateStorageOptionsFromConfiguration(path, settings);

            tableStorage = new TableStorage(persistenceSource, bufferPool);
	        var schemaCreator = new SchemaCreator(configuration, tableStorage, Output, Log);
			schemaCreator.CreateSchema();
			schemaCreator.SetupDatabaseIdAndSchemaVersion();
			schemaCreator.UpdateSchemaIfNecessary();

            SetupDatabaseId();
            idGenerator = new IdGenerator(tableStorage);
        }

        private void SetupDatabaseId()
        {
	        Id = tableStorage.Id;
        }

        public void Batch(Action<IStorageActionsAccessor> action)
        {
            if (Id == Guid.Empty)
                throw new InvalidOperationException("Cannot use Storage before Initialize was called");

            disposerLock.EnterReadLock();
            try
            {
                if (disposed)
                {
                    Trace.WriteLine("TransactionalStorage.Batch was called after it was disposed, call was ignored.");
                    return; // this may happen if someone is calling us from the finalizer thread, so we can't even throw on that
                }

                ExecuteBatch(action);
            }
            catch (Exception e)
            {
                if (disposed)
                {
                    Trace.WriteLine("TransactionalStorage.Batch was called after it was disposed, call was ignored.");
                    return; // this may happen if someone is calling us from the finalizer thread, so we can't even throw on that
                }

                if (e.InnerException is VoronExceptions.ConcurrencyException)
                    throw new ConcurrencyException("Concurrent modification to the same file are not allowed", e.InnerException);

                throw;
            }
            finally
            {
                disposerLock.ExitReadLock();
                current.Value = null;
            }
        }

        private void ExecuteBatch(Action<IStorageActionsAccessor> action)
        {
            if (current.Value != null)
            {
                action(current.Value);
                return;
            }

            using (var snapshot = tableStorage.CreateSnapshot())
            {
                var writeBatchRef = new Reference<WriteBatch>();
                try
                {
                    writeBatchRef.Value = new WriteBatch { DisposeAfterWrite = false };
                    using (var storageActionsAccessor = new StorageActionsAccessor(tableStorage, writeBatchRef, snapshot, idGenerator, bufferPool))
                    {
                        current.Value = storageActionsAccessor;

                        action(storageActionsAccessor);
                        storageActionsAccessor.Commit();

                        tableStorage.Write(writeBatchRef.Value);
                    }
                }
                finally
                {
                    if (writeBatchRef.Value != null)
                    {
                        writeBatchRef.Value.Dispose();
                    }
                }
            }
        }

		private void Output(string message)
		{
			Log.Info(message);
			Console.Write(message);
			Console.WriteLine();
		}
    }
}