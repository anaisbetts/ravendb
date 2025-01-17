﻿using System;
using System.Collections.Generic;
using System.Linq;

using Raven.Tests.Common;
using Raven.Tests.Helpers;

using Xunit;

using System.Text.RegularExpressions;
using Raven.Client.Indexes;

namespace Raven.Tests.MailingList
{
	public class AlexanderLapa : RavenTestBase
	{
		[Fact]
		public void ExecuteIndex_Success()
		{
			using (var store = NewDocumentStore())
			{
				new ResourceViewModels_ByTag().Execute(store);
			}
		}

		[Fact]
		public void QueryIndex_Success()
		{
			using (var store = NewDocumentStore())
			{
				new ResourceViewModels_ByTag().Execute(store);

				using (var session = store.OpenSession())
				{
					var p1 = "abracadabra";
					var p2 = "abracadabra2";
					var p3 = "abracadabra3";

					var res1 = new Resource
					{
						AliasIds = new List<string> {p1},
						Caption = "Awesome test test",
						Comments = new List<Comment>
						{
							new Comment
							{
								Author = "Vasia",
								Text = "This is #awesome"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #awesome"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #awesome"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #bad"
							},
						}

					};

					var res2 = new Resource
					{
						AliasIds = new List<string> {p2},
						Caption = "Bad test test",
						Comments = new List<Comment>
						{
							new Comment
							{
								Author = "Vasia",
								Text = "This is #bad"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #bad"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #bad"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is #awesome"
							},
						}

					};

					var res3 = new Resource
					{
						AliasIds = new List<string> {p3},
						Caption = "No test test",
						Comments = new List<Comment>
						{
							new Comment
							{
								Author = "Vasia",
								Text = "This is no"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is no"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is no"
							},
							new Comment
							{
								Author = "Vasia",
								Text = "This is no"
							},
						}

					};
					session.Store(res1);
					session.Store(res2);
					session.Store(res3);

					var alias1 = new Alias
					{
						Id = p1,
						Criteria = new Criteria {CountryCode = "64"},
						Declaration = "newzealand",
						Resource = new Resource {Id = res1.Id}
					};
					var alias2 = new Alias
					{
						Id = p2,
						Criteria = new Criteria {CountryCode = "7"},
						Declaration = "russia",
						Resource = new Resource {Id = res2.Id}
					};
					var alias3 = new Alias
					{
						Id = p3,
						Criteria = new Criteria {CountryCode = "380"},
						Declaration = "ukraine",
						Resource = new Resource {Id = res3.Id}
					};

					session.Store(alias1);
					session.Store(alias2);
					session.Store(alias3);

					session.SaveChanges();

					var result = session.Query<ResourceViewModel, ResourceViewModels_ByTag>()
					                             .Customize(x => x.WaitForNonStaleResults())
					                             .ToList();

					WaitForUserToContinueTheTest(store);
					Assert.Empty(store.DocumentDatabase.Statistics.Errors);
					Assert.NotEmpty(result);
				}
			}
		}

		public class ResourceViewModels_ByTag : AbstractIndexCreationTask<Resource, ResourceViewModel>
		{
			public ResourceViewModels_ByTag()
			{
				Map = resources => from r in resources
				                   select new
				                   {
					                   Caption = r.Caption,
					                   Declarations = r.AliasIds.Select(LoadDocument<Alias>).ToDictionary(a => a.Criteria.CountryCode, a => a.Declaration),
					                   TopTags = (from c in r.Comments
					                              from tag in (from Match m in Regex.Matches(@"\b#\w\w+", c.Text) select m.Value).ToList()
					                              select new TopTag {Name = tag, Count = 1})
				                   };

				Reduce = results => from res in results
				                    group res by res.Caption
				                    into resgroup
				                    from r in resgroup
				                    select new
				                    {
					                    Caption = r.Caption,
					                    Declarations = r.Declarations,
					                    TopTags = from t in r.TopTags
					                              group t by t.Name
					                              into g
					                              select new TopTag {Name = g.Key, Count = g.Sum(x => x.Count)}
				                    };
			}
		}

		public class ResourceViewModel
		{
			public string Caption { get; set; }
			public Dictionary<string, string> Declarations { get; set; }
			public List<TopTag> TopTags { get; set; }
		}

		public class TopTag
		{
			public string Name { get; set; }
			public int Count { get; set; }
		}

		public class Resource : Doc
		{
			public List<string> AliasIds { get; set; }
			public string Caption { get; set; }
			public List<Comment> Comments { get; set; }
		}

		public class Doc
		{
			public string Id { get; set; }
		}

		public class Criteria
		{
			public string CountryCode { get; set; }
		}

		public class Comment
		{
			public string Author { get; set; }
			public string Text { get; set; }
		}

		public class Alias : Doc
		{
			public Resource Resource { get; set; }
			public Criteria Criteria { get; set; }
			public string Declaration { get; set; }
		}
	}

}