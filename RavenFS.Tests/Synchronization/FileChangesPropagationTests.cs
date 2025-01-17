﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.RavenFS;
using Raven.Database.Server.RavenFS.Extensions;
using RavenFS.Tests.Synchronization.IO;
using Xunit;
using Raven.Json.Linq;

namespace RavenFS.Tests.Synchronization
{
    public class FileChangesPropagationTests : RavenFsTestBase
	{
		[Fact]
		public async Task File_rename_should_be_propagated()
		{
			var content = new MemoryStream(new byte[] {1, 2, 3});

			var server1 = NewClient(0);
			var server2 = NewClient(1);
		    var server3 = NewClient(2);

			content.Position = 0;
            await server1.UploadAsync("test.bin", new RavenJObject { { "test", "value" } }, content);

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			Assert.Null(server1.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOnSynchronization(server2, server3);

			Assert.Null(server2.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOffSynchronization(server1);

            await server1.RenameAsync("test.bin", "rename.bin");

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			var secondServer1Synchronization = await server1.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer1Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.Rename, secondServer1Synchronization[0].Reports.ToArray()[0].Type);

			var secondServer2Synchronization = await server2.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer2Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.Rename, secondServer2Synchronization[0].Reports.ToArray()[0].Type);

			// On all servers should be file named "rename.bin"
            var server1BrowseResult = await server1.BrowseAsync();
            Assert.Equal(1, server1BrowseResult.Count());
            Assert.Equal("rename.bin", server1BrowseResult.First().Name);

            var server2BrowseResult = await server2.BrowseAsync();
            Assert.Equal(1, server2BrowseResult.Count());
            Assert.Equal("rename.bin", server2BrowseResult.First().Name);

            var server3BrowseResult = await server3.BrowseAsync();
            Assert.Equal(1, server3BrowseResult.Count());
            Assert.Equal("rename.bin", server3BrowseResult.First().Name);
		}

		[Fact]
		public async Task File_content_change_should_be_propagated()
		{
			var buffer = new byte[1024*1024*2]; // 2 MB
			new Random().NextBytes(buffer);

			var content = new MemoryStream(buffer);
			var changedContent = new RandomlyModifiedStream(content, 0.02);

			var server1 = NewClient(0);
			var server2 = NewClient(1);
            var server3 = NewClient(2);

			content.Position = 0;
            await server1.UploadAsync("test.bin", new RavenJObject { { "test", "value" } }, content);
			
			Assert.Equal(1, server1.StatsAsync().Result.FileCount);

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			Assert.Null(server1.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);
			Assert.Equal(1, server2.StatsAsync().Result.FileCount);

			SyncTestUtils.TurnOnSynchronization(server2, server3);

			Assert.Null(server2.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);
			Assert.Equal(1, server3.StatsAsync().Result.FileCount);

			SyncTestUtils.TurnOffSynchronization(server1);

			content.Position = 0;
            await server1.UploadAsync("test.bin", changedContent);

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			var secondServer1Synchronization = await server1.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer1Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.ContentUpdate, secondServer1Synchronization[0].Reports.ToArray()[0].Type);

			var secondServer2Synchronization = await server2.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer2Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.ContentUpdate, secondServer2Synchronization[0].Reports.ToArray()[0].Type);

			// On all servers should have the same content of the file
			string server1Md5;
			using (var resultFileContent = new MemoryStream())
			{
				server1.DownloadAsync("test.bin", resultFileContent).Wait();
				resultFileContent.Position = 0;
				server1Md5 = resultFileContent.GetMD5Hash();
			}

			string server2Md5;
			using (var resultFileContent = new MemoryStream())
			{
				server2.DownloadAsync("test.bin", resultFileContent).Wait();
				resultFileContent.Position = 0;
				server2Md5 = resultFileContent.GetMD5Hash();
			}

			string server3Md5;
			using (var resultFileContent = new MemoryStream())
			{
				server3.DownloadAsync("test.bin", resultFileContent).Wait();
				resultFileContent.Position = 0;
				server3Md5 = resultFileContent.GetMD5Hash();
			}

			Assert.Equal(server1Md5, server2Md5);
			Assert.Equal(server2Md5, server3Md5);

			Assert.Equal(1, server1.StatsAsync().Result.FileCount);
			Assert.Equal(1, server2.StatsAsync().Result.FileCount);
			Assert.Equal(1, server3.StatsAsync().Result.FileCount);
		}

		[Fact]
		public async Task File_delete_should_be_propagated()
		{
			var content = new MemoryStream(new byte[] {1, 2, 3});

			var server1 = NewClient(0);
			var server2 = NewClient(1);
            var server3 = NewClient(2);

			content.Position = 0;
            server1.UploadAsync("test.bin", new RavenJObject { { "test", "value" } }, content).Wait();

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			Assert.Null(server1.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOnSynchronization(server2, server3);

			Assert.Null(server2.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOffSynchronization(server1);

			server1.DeleteAsync("test.bin").Wait();

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			var secondServer1Synchronization = await server1.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer1Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.Delete, secondServer1Synchronization[0].Reports.ToArray()[0].Type);

			var secondServer2Synchronization = await server2.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer2Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.Delete, secondServer2Synchronization[0].Reports.ToArray()[0].Type);

			// On all servers should not have any file
			Assert.Equal(0, server1.BrowseAsync().Result.Count());
			Assert.Equal(0, server1.StatsAsync().Result.FileCount);

			Assert.Equal(0, server2.BrowseAsync().Result.Count());
			Assert.Equal(0, server2.StatsAsync().Result.FileCount);

			Assert.Equal(0, server3.BrowseAsync().Result.Count());
			Assert.Equal(0, server3.StatsAsync().Result.FileCount);
		}

		[Fact]
		public async Task Metadata_change_should_be_propagated()
		{
			var content = new MemoryStream(new byte[] {1, 2, 3});

			var server1 = NewClient(0);
			var server2 = NewClient(1);
            var server3 = NewClient(2);

			content.Position = 0;
            await server1.UploadAsync("test.bin", new RavenJObject { { "test", "value" } }, content);

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			Assert.Null(server1.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOnSynchronization(server2, server3);

			Assert.Null(server2.Synchronization.SynchronizeDestinationsAsync().Result[0].Exception);

			SyncTestUtils.TurnOffSynchronization(server1);

            await server1.UpdateMetadataAsync("test.bin", new RavenJObject { { "new_test", "new_value" } });

			SyncTestUtils.TurnOnSynchronization(server1, server2);

			var secondServer1Synchronization = await server1.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer1Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.MetadataUpdate, secondServer1Synchronization[0].Reports.ToArray()[0].Type);

			var secondServer2Synchronization = await server2.Synchronization.SynchronizeDestinationsAsync();
			Assert.Null(secondServer2Synchronization[0].Exception);
			Assert.Equal(SynchronizationType.MetadataUpdate, secondServer2Synchronization[0].Reports.ToArray()[0].Type);

			// On all servers should be file named "rename.bin"
			var server1Metadata = await server1.GetMetadataForAsync("test.bin");
			var server2Metadata = await server2.GetMetadataForAsync("test.bin");
			var server3Metadata = await server3.GetMetadataForAsync("test.bin");

            Assert.Equal("new_value", server1Metadata.Value<string>("new_test"));
			Assert.Equal("new_value", server2Metadata.Value<string>("new_test"));
			Assert.Equal("new_value", server3Metadata.Value<string>("new_test"));
		}
	}
}