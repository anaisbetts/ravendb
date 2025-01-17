//-----------------------------------------------------------------------
// <copyright file="CreateUpdateDeleteDocuments.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Raven.Abstractions.Data;
using Raven.Client.Embedded;
using Raven.Json.Linq;
using Raven.Database;
using Raven.Database.Config;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Storage
{
	public class CreateUpdateDeleteDocuments : RavenTest
	{
		private readonly EmbeddableDocumentStore store;
		private readonly DocumentDatabase db;

		public CreateUpdateDeleteDocuments()
		{
			store = NewDocumentStore();
			db = store.DocumentDatabase;
		}

		public override void Dispose()
		{
			store.Dispose();
			base.Dispose();
		}

		[Fact]
		public void When_creating_document_with_id_specified_will_return_specified_id()
		{
			var documentId = db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"),
			                        new RavenJObject(), null);
			Assert.Equal("1", documentId.Key);
		}

		[Fact]
		public void Can_get_id_from_document_metadata()
		{
			db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"),
			       new RavenJObject(), null);
			Assert.Equal("1", db.Documents.Get("1", null).Metadata["@id"].Value<string>());
		}

		[Fact]
		public void When_creating_document_with_no_id_specified_will_return_guid_as_id()
		{
			var documentId = db.Documents.Put(null, Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"),
									new RavenJObject(), null);
			Assert.DoesNotThrow(() => new Guid(documentId.Key));
		}

		[Fact]
		public void Can_create_and_read_document()
		{
			db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{  first_name: 'ayende', last_name: 'rahien'}"), new RavenJObject(), null);
			var document = db.Documents.Get("1", null).ToJson();

			Assert.Equal("ayende", document.Value<string>("first_name"));
			Assert.Equal("rahien", document.Value<string>("last_name"));
		}

		[Fact]
		public void Can_edit_document()
		{
			db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"), new RavenJObject(), null);

			db.Documents.Put("1", db.Documents.Get("1", null).Etag, RavenJObject.Parse("{ first_name: 'ayende2', last_name: 'rahien2'}"), new RavenJObject(), null);
			var document = db.Documents.Get("1", null).ToJson();

			Assert.Equal("ayende2", document.Value<string>("first_name"));
			Assert.Equal("rahien2", document.Value<string>("last_name"));
		}

		[Fact]
		public void Can_delete_document()
		{
			db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"), new RavenJObject(), null);
			var document = db.Documents.Get("1", null);
			db.Documents.Delete("1", document.Etag, null);

			Assert.Null(db.Documents.Get("1", null));
		}

		[Fact]
		public void Can_query_document_by_id_when_having_multiple_documents()
		{
			db.Documents.Put("1", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende', last_name: 'rahien'}"), new RavenJObject(), null);
			db.Documents.Put("21", Etag.Empty, RavenJObject.Parse("{ first_name: 'ayende2', last_name: 'rahien2'}"), new RavenJObject(), null);
			var document = db.Documents.Get("21", null).ToJson();

			Assert.Equal("ayende2", document.Value<string>("first_name"));
			Assert.Equal("rahien2", document.Value<string>("last_name"));
		}

		[Fact]
		public void Querying_by_non_existent_document_returns_null()
		{
			Assert.Null(db.Documents.Get("1", null));
		}
	}
}
