#region Copyright and License
// 
// Fizzler - CSS Selector Engine for Microsoft .NET Framework
// Copyright (c) 2009 Atif Aziz, Colin Ramsay. All rights reserved.
// 
// This library is free software; you can redistribute it and/or modify it under 
// the terms of the GNU Lesser General Public License as published by the Free 
// Software Foundation; either version 3 of the License, or (at your option) 
// any later version.
// 
// This library is distributed in the hope that it will be useful, but WITHOUT 
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS 
// FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more 
// details.
// 
// You should have received a copy of the GNU Lesser General Public License 
// along with this library; if not, write to the Free Software Foundation, Inc., 
// 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA 
// 
#endregion

namespace VisualFizzler
{
    #region Imports

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Mime;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;
    using Fizzler;
    using Fizzler.Systems.HtmlAgilityPack;
    using HtmlAgilityPack;
    using Mannex.Net;
    using Mannex.Net.Mime;
    using Microsoft.VisualBasic;
    using OpenWebClient;
    using HtmlDocument = HtmlAgilityPack.HtmlDocument;
    using WebClient = OpenWebClient.WebClient;

    #endregion

    public partial class MainForm : Form
    {
        private static readonly Regex _tagExpression = new Regex(@"\<(?:(?<t>[a-z]+)(?:""[^""]*""|'[^']*'|[^""'>])*|/(?<t>[a-z]+))\>",
            RegexOptions.IgnoreCase
            | RegexOptions.Singleline
            | RegexOptions.Compiled
            | RegexOptions.CultureInvariant);

        private HtmlDocument _document;
        private Match[] _selectorMatches;
        private Uri _lastKnownGoodImportedUrl;

        public MainForm()
        {
            InitializeComponent();
        }

        private void FileExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void FileOpen_Click(object sender, EventArgs e)
        {
            if (_openFileDialog.ShowDialog(this) != DialogResult.OK)
                return;

            var html = File.ReadAllText(_openFileDialog.FileName);
            var document = new HtmlDocument();
            document.LoadHtml2(html);
            Open(document, html);
        }

        private void ImportFromWebMenu_Click(object sender, EventArgs args)
        {
            Uri url = null;

            var input = _lastKnownGoodImportedUrl != null 
                      ? _lastKnownGoodImportedUrl.ToString() 
                      : string.Empty;

            do
            {
                input = Interaction.InputBox("Enter URL:", "Import From Web", input,
                    (int)(Location.X + (Size.Height / 10f)),
                    (int)(Location.Y + (Size.Height / 10f))).Trim();

                if (string.IsNullOrEmpty(input))
                    return;

                //
                // If some entered just the DNS name to get the home page 
                // then we prepend "http://" for the user to prevent typing.
                //
                // http://www.shauninman.com/archive/2006/05/08/validating_domain_names
                //

                if (Regex.IsMatch(input, @"^([a-z0-9] ([-a-z0-9]*[a-z0-9])? \.)+ 
                                            ( (a[cdefgilmnoqrstuwxz]|aero|arpa)
                                              |(b[abdefghijmnorstvwyz]|biz)
                                              |(c[acdfghiklmnorsuvxyz]|cat|com|coop)
                                              |d[ejkmoz]
                                              |(e[ceghrstu]|edu)
                                              |f[ijkmor]
                                              |(g[abdefghilmnpqrstuwy]|gov)
                                              |h[kmnrtu]
                                              |(i[delmnoqrst]|info|int)
                                              |(j[emop]|jobs)
                                              |k[eghimnprwyz]
                                              |l[abcikrstuvy]
                                              |(m[acdghklmnopqrstuvwxyz]|mil|mobi|museum)
                                              |(n[acefgilopruz]|name|net)
                                              |(om|org)
                                              |(p[aefghklmnrstwy]|pro)
                                              |qa
                                              |r[eouw]
                                              |s[abcdeghijklmnortvyz]
                                              |(t[cdfghjklmnoprtvwz]|travel)
                                              |u[agkmsyz]
                                              |v[aceginu]
                                              |w[fs]
                                              |y[etu]
                                              |z[amw])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture))
                {
                    input = "http://" + input;
                }

                if (!Uri.IsWellFormedUriString(input, UriKind.Absolute))
                    MessageBox.Show(this, "The entered URL does not appear to be correctly formatted.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                else
                    url = new Uri(input, UriKind.Absolute);
            }
            while (url == null);

            //
            // Download
            //

            string content;
            var wc = new WebClient();
            bool proxyAuthAttempted = false;

            while (true)
            {
                try
                {
                    using (CurrentCursorScope.EnterWait())
                        content = wc.DownloadStringUsingResponseEncoding(url);
                    break;
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.ProtocolError 
                        && !proxyAuthAttempted)
                    {
                        var httpResponse = e.Response as HttpWebResponse;
                        if (httpResponse != null
                            && HttpStatusCode.ProxyAuthenticationRequired == httpResponse.StatusCode)
                        {
                            proxyAuthAttempted = true;
                            wc.AddHttpWebRequestModifier(hwr => hwr.Proxy.Credentials = CredentialCache.DefaultCredentials);
                            continue;
                        }
                    }

                    Program.ShowExceptionDialog(e, "Import Error", this);
                    return;
                }
            }

            //
            // Make sure it's HTML otherwise get confirmation to proceed.
            //

            var typeHeader = wc.ResponseHeaders[HttpResponseHeader.ContentType];
            var contentType = new ContentType(typeHeader);
            if (string.IsNullOrEmpty(typeHeader) || !contentType.IsHtml())
            {
                var msg = string.Format(
                    "The downloaded resource is \u201c{0}\u201d rather than HTML, "
                    + "which could produce undesired results. Proceed anyway?",
                    contentType.MediaType);

                if (DialogResult.Yes != MessageBox.Show(this, msg, "Non-HTML Content", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation))
                    return;
            }

            //
            // If it's too big (> 512KB), get confirmation to proceed.
            //

            var lengthHeader = wc.ResponseHeaders[HttpResponseHeader.ContentLength];
            long size;
            if (long.TryParse(lengthHeader, NumberStyles.None, CultureInfo.InvariantCulture, out size)
                && size > 512 * 1024)
            {
                var msg = string.Format(
                    "The downloaded resource is rather large ({0} bytes). Proceed anyway?",
                    size.ToString("N0"));

                if (DialogResult.Yes != MessageBox.Show(this, msg, "Large Content", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation))
                    return;
            }

            //
            // Load 'er up!
            //

            using (CurrentCursorScope.EnterWait())
            {
                var document = new HtmlDocument();
                document.LoadHtml2(content);
                Open(document, content);
            }

            _lastKnownGoodImportedUrl = url;
        }

        private void SelectorBox_TextChanged(object sender, EventArgs e)
        {
            Evaluate();
        }

        private void HelpContents_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/atifaziz/Fizzler");
        }

        private void Open(HtmlDocument document, string html)
        {
            _document = document;
            _documentBox.Clear();
            _documentBox.Text = html;
            _selectorMatches = null;
            HighlightMarkup(_documentBox, Color.Blue, Color.FromArgb(163, 21, 21), Color.Red);
            Evaluate();
        }

        private static void HighlightMarkup(RichTextBox rtb, Color tagColor, Color tagNameColor, Color attributeNameColor)
        {
            Debug.Assert(rtb != null);

            foreach (Match tag in _tagExpression.Matches(rtb.Text))
            {
                Highlight(rtb, tag.Index, tag.Length, tagColor, null, null);

                var name = tag.Groups["t"];
                Highlight(rtb, name.Index, name.Length, tagNameColor, null, null);

                var attributes = Regex.Matches(tag.Value, 
                    @"\b([a-z]+)\s*=\s*(?:""[^""]*""|'[^']*'|[^'"">\s]+)", 
                    RegexOptions.IgnoreCase
                    | RegexOptions.Singleline
                    | RegexOptions.CultureInvariant);

                foreach (var attribute in attributes.Cast<Match>().Select(m => m.Groups[1]))
                    Highlight(rtb, tag.Index + attribute.Index, attribute.Length, attributeNameColor, null, null);
            }
        }

        private static void Highlight(RichTextBox rtb, IEnumerable<Match> matches, Color? color, Color? backColor, Font font)
        {
            foreach (var match in matches)
                Highlight(rtb, match.Index, match.Length, color, backColor, font);
        }

        private static void Highlight(RichTextBox rtb, int start, int length, Color? color, Color? backColor, Font font)
        {
            rtb.SelectionStart = start;
            rtb.SelectionLength = length;
            if (color != null) rtb.SelectionColor = color.Value;
            if (backColor != null) rtb.SelectionBackColor = backColor.Value;
            if (font != null) rtb.SelectionFont = font;
        }

        private void Evaluate()
        {
            _selectorMatches = Evaluate(_document, _selectorBox, _matchBox, _helpBox, _statusLabel, _selectorMatches, _documentBox);
        }

        private static Match[] Evaluate(HtmlDocument document, Control tb, ListBox lb, Control hb, ToolStripItem status, IEnumerable<Match> oldMatches, RichTextBox rtb)
        {
            var input = tb.Text.Trim();
            tb.ForeColor = SystemColors.WindowText;
            
            var elements = new HtmlNode[0];
            
            if (string.IsNullOrEmpty(input))
            {
                status.Text = "Ready";
                hb.Text = null;
            }
            else
            {
                try
                {
                    //
                    // Simple way to query for elements:
                    //
                    // nodes = document.DocumentNode.QuerySelectorAll(input).ToArray();
                    //
                    // However, we want to generate the human readable text and
                    // the element selector in a single pass so go the bare metal way 
                    // here to make all the parties to talk to each other.
                    //
                    
                    var generator = new SelectorGenerator<HtmlNode>(new HtmlNodeOps());
                    var helper = new HumanReadableSelectorGenerator();
                    Parser.Parse(input, new SelectorGeneratorTee(generator, helper));
                    if (document != null)
                        elements = generator.Selector(Enumerable.Repeat(document.DocumentNode, 1)).ToArray();
                    hb.Text = helper.Text;

                    status.Text = "Matches: " + elements.Length.ToString("N0");
                }
                catch (FormatException e)
                {
                    tb.ForeColor = Color.FromKnownColor(KnownColor.Red);
                    status.Text = "Error: " + e.Message;
                    hb.Text = "Oops! " + e.Message;
                }
            }
            
            if (oldMatches != null)
                Highlight(rtb, oldMatches, null, SystemColors.Info, null);
        
            lb.BeginUpdate();
            try
            {
                lb.Items.Clear();
                if (!elements.Any())
                    return new Match[0];

                var html = rtb.Text;
                var matches  = new List<Match>(elements.Length);
                foreach (var element in elements)
                {
                    var index = rtb.GetFirstCharIndexFromLine(element.Line - 1) + element.LinePosition - 1;
                    var match = _tagExpression.Match(html, index);
                    if (match.Success)
                        matches.Add(match);
                }
                
                Highlight(rtb, matches, null, Color.Yellow, null);
                
                lb.Items.AddRange(elements.Select(n => n.GetBeginTagString()).ToArray());
                
                return matches.ToArray();
            }
            finally
            {
                lb.EndUpdate();
            }
        }
    }
}
