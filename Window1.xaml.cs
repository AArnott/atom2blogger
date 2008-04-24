using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Google.GData.Client;
using System.Net;
using System.Xml.XPath;
using System.Xml;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Atom2Blogger {
	public partial class Window1 : Window {
		public Window1() {
			InitializeComponent();

			sourcePostsListView.ItemsSource = oldBlogEntries;
			destinationPostsListView.ItemsSource = newBlogEntries;
		}

		const string atomNamespace = "http://www.w3.org/2005/Atom";
		Uri newFeedUri { get { return new Uri(destinationFeed.Text); } }
		ObservableCollection<AtomEntry> oldBlogEntries = new ObservableCollection<AtomEntry>();
		ObservableCollection<AtomEntry> newBlogEntries = new ObservableCollection<AtomEntry>();
		Dictionary<AtomEntry, AtomEntry> oldToNewEntryMapping = new Dictionary<AtomEntry, AtomEntry>();
		Dictionary<Uri, Uri> oldToNewEntryUriMapping = new Dictionary<Uri, Uri>();

		Service getNewService() {
			Service service = new Service("blogger", "Atom2Blogger");
			service.Credentials = new NetworkCredential(destinationUsernameBox.Text, destinationPasswordBox.Password);
			return service;
		}
		AtomFeed getNewFeed() {
			Service service = getNewService();
			FeedQuery query = new FeedQuery(destinationFeed.Text);
			query.NumberToRetrieve = 100;
			AtomFeed feed = service.Query(query);
			return feed;
		}

		void downloadOldPosts() {
			oldBlogEntries.Clear();
			var req = HttpWebRequest.Create(sourceFeed.Text);
			req.Credentials = new NetworkCredential(sourceUsernameBox.Text, sourcePasswordBox.Password);
			var resp = req.GetResponse();
			var doc = new XPathDocument(resp.GetResponseStream());
			var xpath = doc.CreateNavigator();
			XmlNamespaceManager nsmgr = new XmlNamespaceManager(xpath.NameTable);
			nsmgr.AddNamespace("atom", atomNamespace);
			foreach (XPathNavigator entryXml in xpath.Select("/atom:feed/atom:entry", nsmgr)) {
				AtomEntry entry = new AtomEntry();
				entry.Title.Text = entryXml.SelectSingleNode("atom:title", nsmgr).Value;
				entry.Published = entryXml.SelectSingleNode("atom:published", nsmgr).ValueAsDateTime;
				entry.Updated = entryXml.SelectSingleNode("atom:updated", nsmgr).ValueAsDateTime;
				entry.Content.Type = entryXml.SelectSingleNode("atom:content/@type", nsmgr).Value;
				entry.Content.Content = entryXml.SelectSingleNode("atom:content", nsmgr).Value;
				foreach (XPathNavigator categoryXml in entryXml.Select("atom:category/@term", nsmgr)) {
					var newCat = new AtomCategory(categoryXml.Value);
					newCat.Scheme = "http://www.blogger.com/atom/ns#";
					entry.Categories.Add(newCat);
				}
				entry.SelfUri = new AtomUri(entryXml.SelectSingleNode("atom:link/@href", nsmgr).Value);

				oldBlogEntries.Add(entry);
			}
		}
		void downloadNewPosts() {
			newBlogEntries.Clear();

			AtomFeed feed = getNewFeed();
			foreach (AtomEntry entry in feed.Entries) {
				newBlogEntries.Add(entry);
			}
		}
		void createMapping() {
			oldToNewEntryMapping.Clear();
			foreach (AtomEntry oldEntry in oldBlogEntries) {
				AtomEntry newEntry = newBlogEntries.SingleOrDefault(e =>
					(e.Published - oldEntry.Published) < TimeSpan.FromSeconds(2) &&
					e.Title.Text.Equals(oldEntry.Title.Text, StringComparison.OrdinalIgnoreCase));
				if (newEntry != null) {
					oldToNewEntryMapping[oldEntry] = newEntry;
					oldToNewEntryUriMapping[new Uri(oldEntry.SelfUri.Content)] =
						new Uri(newEntry.Links[0].HRef.Content);
				}
			}
		}

		string filterTrackingImage(string content) {
			return Regex.Replace(content, @"\<img src="".+/aggbug\.aspx\?PostID=\w+"" width=""1"" height=""1""\>", "");
		}

		void generateRedirectsButton_Click(object sender, RoutedEventArgs e) {
			foreach (var entryMapping in oldToNewEntryUriMapping) {
				Uri oldUri = entryMapping.Key;
				Uri newUri = entryMapping.Value;
				string redirectFilePath = Path.Combine(redirectPagesLocationBox.Text, oldUri.AbsolutePath.Substring(1));
				string redirectedDirectoryPath = Path.GetDirectoryName(redirectFilePath);
				if (!Directory.Exists(redirectedDirectoryPath)) {
					Directory.CreateDirectory(redirectedDirectoryPath);
				}
				using (StreamWriter sw = File.CreateText(redirectFilePath)) {
					if (Path.GetExtension(redirectFilePath).Equals(".aspx", StringComparison.OrdinalIgnoreCase)) {
						sw.WriteLine("<%@ Page Language=\"C#\" %>");
						sw.WriteLine("<% Response.Redirect(\"{0}\"); %>", newUri);
					} else {
						sw.WriteLine("<html>");
						sw.WriteLine("	<head>");
						sw.WriteLine("		<meta http-equiv=\"refresh\" content=\"0;url={0}\" />", newUri);
						sw.WriteLine("	</head>");
						sw.WriteLine("</html>");
					}
				}
			}

			// Create feed redirects

			// Also create default.aspx/.htm redirects in all directories

		}
		void fixContentButton_Click(object sender, RoutedEventArgs e) {
			Cursor = Cursors.Wait;
			try {
				foreach (AtomEntry entry in getNewFeed().Entries) {
					string changedContent = entry.Content.Content;
					if (removeTrackingImage.IsChecked.Value) {
						changedContent = filterTrackingImage(changedContent);
					}
					if (fixInternalLinks.IsChecked.Value) {
						foreach (var urlMapping in oldToNewEntryUriMapping) {
							string oldUrl = urlMapping.Key.ToString();
							string newUrl = urlMapping.Value.ToString();
							changedContent = Regex.Replace(changedContent, Regex.Escape(oldUrl),
								newUrl, RegexOptions.IgnoreCase);
						}
					}
					// Only update content field if a change occurred, so we don't upload unchanged content.
					if (changedContent != entry.Content.Content) {
						entry.Content.Content = changedContent;
					}
					if (migrateCategories.IsChecked.Value) {
						// Fix categories
						if (entry.Categories.Count == 0) {
							AtomEntry oldPost = oldBlogEntries.SingleOrDefault(
								oldEntry => oldEntry.Title.Text == entry.Title.Text &&
								(oldEntry.Published - entry.Published) < TimeSpan.FromDays(1));
							if (oldPost != null) {
								foreach (AtomCategory cat in oldPost.Categories) {
									AtomCategory newCat = new AtomCategory(cat.Term);
									newCat.Scheme = "http://www.blogger.com/atom/ns#";
									entry.Categories.Add(newCat);
								}
							}
						}
					}

					if (entry.IsDirty()) {
						AtomEntry updatedEntry = entry.Update();
					}
				}
			} finally {
				Cursor = Cursors.Arrow;
			}
		}

		void connectToOldBlog_Click(object sender, RoutedEventArgs e) {
			Mouse.OverrideCursor = Cursors.Wait;
			try {
				downloadOldPosts();
			} finally {
				Mouse.OverrideCursor = null;
			}
		}
		void connectToNewBlog_Click(object sender, RoutedEventArgs e) {
			Mouse.OverrideCursor = Cursors.Wait;
			try {
				downloadNewPosts();
			} finally {
				Mouse.OverrideCursor = null;
			}
		}
		void transferPosts_Click(object sender, RoutedEventArgs e) {
			Mouse.OverrideCursor = Cursors.Wait;
			try {
				createMapping();

				Service service = getNewService();
				foreach (AtomEntry oldEntry in oldBlogEntries.Where(entry => !oldToNewEntryMapping.ContainsKey(entry))) {
					oldToNewEntryMapping[oldEntry] = service.Insert(newFeedUri, oldEntry);
				}

				// Get an updated list of posts on the new blog.
				downloadNewPosts();
				createMapping();
			} finally {
				Mouse.OverrideCursor = null;
			}
		}
	}
}
