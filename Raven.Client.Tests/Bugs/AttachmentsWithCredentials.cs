using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Raven.Client.Document;
using Raven.Client.Tests.Document;
using Raven.Database;
using Raven.Server;
using Xunit;

namespace Raven.Client.Tests.Bugs
{
    public class AttachmentsWithCredentials : RemoteClientTest
    {
        private readonly string path;

        #region IDisposable Members

        public void Dispose()
        {
            server.Dispose();
            store.Dispose();
            Directory.Delete(path, true);
        }

        #endregion


      private readonly IDocumentStore store;
		private readonly RavenDbServer server;

		public AttachmentsWithCredentials()
		{
		    path = GetPath(DbName);
            server = GetNewServerWithoutAnonymousAccess(8080, path);

			store = new DocumentStore
			{
				Url = "http://localhost:8080"
			}.Initialize();
		}


        [Fact]
        public void CanPutAndGetAttachmentWithAccessModeNone()
        {
            store.DatabaseCommands.PutAttachment("ayende", null, new byte[] {1, 2, 3, 4}, new JObject());

            Assert.Equal(new byte[] {1, 2, 3, 4}, store.DatabaseCommands.GetAttachment("ayende").Data);
        }

        [Fact]
        public void CanDeleteAttachmentWithAccessModeNone()
        {
            store.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 1, 2, 3, 4 }, new JObject());

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, store.DatabaseCommands.GetAttachment("ayende").Data);

            store.DatabaseCommands.DeleteAttachment("ayende", null);

            Assert.Null(store.DatabaseCommands.GetAttachment("ayende"));
        }
    }
}