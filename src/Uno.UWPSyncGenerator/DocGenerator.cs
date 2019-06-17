﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Uno.Extensions;
using static Uno.UWPSyncGenerator.MarkdownStringBuilder;

namespace Uno.UWPSyncGenerator
{
	/// <summary>
	/// Generates documentation about what parts of the UWP contract are currently implemented by Uno.
	/// </summary>
	class DocGenerator : Generator
	{
		private const string DocPath = @"..\..\..\..\..\doc\articles";
		private const string ImplementedViewsFileName = "implemented-views.md";
		private const string ImplementedPath = @"./implemented/";

		private MarkdownStringBuilder _sb;
		private List<PlatformSymbols<INamedTypeSymbol>> _views;
		private IGrouping<INamespaceSymbol, PlatformSymbols<INamedTypeSymbol>>[] _viewsGrouped;
		private HashSet<(string name, string namespaceString)> _kosherFrameworkViews;

		public override void Build(string basePath, string baseName, string sourceAssembly)
		{
			_sb = new MarkdownStringBuilder();

			_sb.AppendComment($"*** This file has been generated by {typeof(DocGenerator).FullName}, do not edit manually. ***");
			_sb.AppendLine();

			_views = new List<PlatformSymbols<INamedTypeSymbol>>();
			base.Build(basePath, baseName, sourceAssembly);

			_viewsGrouped = GroupByNamespace(_views);
			_kosherFrameworkViews = new HashSet<(string name, string namespaceString)>(_views.Select(ps => (ps.UAPSymbol.Name, ps.UAPSymbol.ContainingNamespace.ToDisplayString())));

			using (_sb.Section("List of views implemented in Uno"))
			{
				_sb.AppendParagraph("The Uno.UI assembly includes all types and members from the UWP API (as of the Windows 10 October 2018 Update (17763)). Only some of these are actually implemented. The remainder are marked with the `[NotImplemented]` attribute and will throw an exception at runtime if used.");

				_sb.AppendParagraph("This page lists controls that are currently implemented in Uno. Navigate to individual control entries to see which properties, methods, and events are implemented for a given control.");

				_sb.AppendParagraph($"If you notice incorrect or incomplete information here, please open an {Hyperlink("issue", "https://github.com/nventive/Uno/issues")}.");

				using (_sb.Section("Implemented - all platforms (iOS, Android, WebAssembly, MacOS)"))
				{
					AppendTypes(ps => ps.ImplementedForMain == ImplementedFor.Main, true);
				}
				using (_sb.Section("Implemented - Android + iOS only"))
				{
					AppendTypes(ps => ps.ImplementedForMain == ImplementedFor.Mobile, true);
				}
				using (_sb.Section("Implemented - select platforms")) //These all seem to be lies
				{
					using (_sb.Table("Type", "Supported platforms"))
					{
						foreach (var view in _views)
						{
							if (view.ImplementedForMain != ImplementedFor.Main && view.ImplementedFor != ImplementedFor.Mobile && view.ImplementedFor != ImplementedFor.None)
							{
								_sb.AppendRow(view.UAPSymbol.ToDisplayString(), ToDisplayString(view.ImplementedForMain));
							}
						}
					}
				}
				using (_sb.Section("Not yet implemented"))
				{
					_sb.AppendParagraph($"If there's a specific control you'd like to see implemented, {Hyperlink("create an issue!", "https://github.com/nventive/Uno/issues")}");

					AppendTypes(ps => ps.ImplementedForMain == ImplementedFor.None, false);
				}

				_sb.AppendHorizontalRule();

				_sb.AppendParagraph();
				_sb.AppendParagraph($"Last updated {DateTimeOffset.UtcNow.ToString("f")}.");
			}
			using (var fileWriter = new StreamWriter(Path.Combine(DocPath, ImplementedViewsFileName)))
			{
				fileWriter.Write(_sb.ToString());
			}

			BuildMemberLists();

			void BuildMemberLists()
			{
				Directory.CreateDirectory(Path.Combine(DocPath, ImplementedPath));

				var tocSB = new StringBuilder();

				foreach (var group in _viewsGrouped)
				{
					if (group.All(ps => ps.ImplementedForMain == ImplementedFor.None))
					{
						continue;
					}


					var currentNamespace = group.Key.ToDisplayString();

					foreach (var view in group.Where(ps => ps.ImplementedForMain != ImplementedFor.None).OrderBy(ps => ps.UAPSymbol.Name))
					{
						_sb = new MarkdownStringBuilder();

						_sb.AppendComment($"*** This file has been generated by {typeof(DocGenerator).FullName}, do not edit it manually. ***");
						var viewName = view.UAPSymbol.Name;
						var formattedViewName = $"`{viewName}`";
						using (_sb.Section($"{viewName} : {ConstructBaseClassString(view)}"))
						{
							_sb.AppendParagraph($"*Implemented for:* {ToDisplayString(view.ImplementedForMain)}");

							var baseDocLinkUrl = @"https://docs.microsoft.com/en-us/uwp/api/" + view.UAPSymbol.ToDisplayString().ToLowerInvariant();
							_sb.AppendParagraph($"This document lists all properties, methods, and events of {formattedViewName} that are currently implemented in Uno. See the {Hyperlink("UWP documentation", baseDocLinkUrl)} for help using {formattedViewName}. ");

							var customDocLink = GetCustomDocLink(viewName);
							if (customDocLink != null)
							{
								_sb.AppendParagraph($"{formattedViewName} has Uno-specific documentation {Hyperlink("here", customDocLink)}.");
							}

							var properties = view.UAPSymbol.GetMembers().OfType<IPropertySymbol>().Select(p => GetAllMatchingPropertyMember(view, p)).ToArray();
							var methods = view.UAPSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).Select(m => GetAllMatchingMethods(view, m)).ToArray();
							var events = view.UAPSymbol.GetMembers().OfType<IEventSymbol>().Select(e => GetAllMatchingEvents(view, e)).ToArray();

							var allMethods = view.UAPSymbol.GetMembers().OfType<IMethodSymbol>().ToArray();
							var allMethodNames = allMethods.Select(m => m.Name).ToArray();
							var methodNames = view.UAPSymbol
								.GetMembers()
								.OfType<IMethodSymbol>()
								.Where(m => m.MethodKind == MethodKind.Ordinary &&
									!(m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) // Filter out explicit event add/remove methods (associated with routed events). These should already be filtered out by the MethodKind.Ordinary check but for some reason, on the build server only, aren't.
								)
								.Select(m => m.Name)
								.ToArray();

							AppendImplementedMembers("properties", properties);
							AppendImplementedMembers("methods", methods);
							AppendImplementedMembers("events", events);

							_sb.AppendHorizontalRule();

							_sb.AppendParagraph($"Below are all properties, methods, and events of {formattedViewName} that are **not** currently implemented in Uno.");

							AppendNotImplementedMembers("properties", properties);
							AppendNotImplementedMembers("methods", methods);
							AppendNotImplementedMembers("events", events);

							_sb.AppendHorizontalRule();

							_sb.AppendParagraph();
							_sb.AppendParagraph($"Last updated {DateTimeOffset.UtcNow.ToString("f")}.");

							void AppendImplementedMembers<T>(string memberType, IEnumerable<PlatformSymbols<T>> members) where T : ISymbol
							{
								var implemented = members.Where(ps => ps.ImplementedForMain != ImplementedFor.None);
								if (implemented.None())
								{
									return;
								}
								using (_sb.Table($"Implemented {memberType} ", ""))
								{
									foreach (var member in implemented)
									{
										var linkUrl = $"{baseDocLinkUrl}.{member.UAPSymbol.Name.ToLowerInvariant()}";
										var implementedQualifier = member.ImplementedForMain != ImplementedFor.Main ? $" *({ToDisplayString(member.ImplementedForMain)})* " : "";
										_sb.AppendRow(Hyperlink(member.UAPSymbol.ToDisplayString(DisplayFormat), linkUrl), implementedQualifier);
									}
									_sb.AppendParagraph();
								}
							}

							void AppendNotImplementedMembers<T>(string memberType, IEnumerable<PlatformSymbols<T>> members) where T : ISymbol
							{
								var notImplemented = members.Where(ps => ps.ImplementedForMain != ImplementedFor.Main);
								if (notImplemented.None())
								{
									return;
								}
								using (_sb.Table($"Not implemented {memberType}", ""))
								{
									foreach (var member in notImplemented)
									{
										var linkUrl = $"{baseDocLinkUrl}.{member.UAPSymbol.Name.ToLowerInvariant()}";
										var notImplementedQualifier = member.ImplementedForMain != ImplementedFor.None ? $" *({ToDisplayString(member.ImplementedForMain ^ ImplementedFor.Main)})* " : "";
										_sb.AppendRow(Hyperlink(member.UAPSymbol.ToDisplayString(DisplayFormat), linkUrl), notImplementedQualifier);
									}
									_sb.AppendParagraph();
								}
							}
						}
						using (var fileWriter = new StreamWriter(Path.Combine(DocPath, GetImplementedMembersFilename(view.UAPSymbol))))
						{
							fileWriter.Write(_sb.ToString());
						}

						// Build TOC in implemented folder
						tocSB.AppendLineInvariant($"- name: {viewName}");
						tocSB.AppendLineInvariant($"  href: ../{GetImplementedMembersFilename(view.UAPSymbol)}");
					}
				}

				using (var fileWriter = new StreamWriter(Path.Combine(DocPath, ImplementedPath, "toc.yml")))
				{
					fileWriter.Write(tocSB.ToString());
				}
			}

			void AppendTypes(Func<PlatformSymbols<INamedTypeSymbol>, bool> appendCondition, bool showLinks)
			{
				_sb.AppendLine();
				foreach (var group in _viewsGrouped)
				{
					if (group.None(appendCondition))
					{
						continue;
					}

					using (_sb.Table(group.Key.ToDisplayString(), ""))
					{
						var cells = group
							.Where(appendCondition)
							.OrderBy(ps => ps.UAPSymbol.Name)
							.Select(ps => showLinks ?
								Hyperlink(ps.UAPSymbol.Name, GetImplementedMembersFilename(ps.UAPSymbol)) :
								ps.UAPSymbol.Name
							)
							.ToList();
						_sb.AppendCells(cells);
					}
				};
			}
		}

		protected override void ProcessType(INamedTypeSymbol type, INamespaceSymbol ns)
		{
			var allSymbols = GetAllSymbols(type);
			if (IsViewType(type))
			{
				_views.Add(allSymbols);
			}
		}

		/// <summary>
		/// Check whether a type from the UWP assembly is a view.
		/// </summary>
		/// <returns>True if the type inherits from UIElement.</returns>
		private bool IsViewType(INamedTypeSymbol type)
		{
			if (type == null)
			{
				return false;
			}

			if (type == UIElementSymbol)
			{
				return true;
			}

			return IsViewType(type.BaseType);
		}

		private string ConstructBaseClassString(PlatformSymbols<INamedTypeSymbol> view)
		{
			var uniqueBaseTypes = AllSymbols()
				.Where(s => s.Symbol != null)
				.Select(s => (s.Symbol.BaseType.Name, s.Symbol.BaseType.ContainingNamespace.ToDisplayString()))
				.Distinct()
				.ToArray();
			if (uniqueBaseTypes.Length == 1)
			{
				var tuple = uniqueBaseTypes.Single();
				return Hyperlink(tuple.Item1, GetLinkTarget(tuple));
			}
			else
			{
				var output = "";
				foreach (var tuple in uniqueBaseTypes)
				{
					var link = GetLinkTarget(tuple);
					var name = link != null ?
						Hyperlink(tuple.Item1, link) :
						tuple.Item1;
					var matchingPlatforms = AllSymbols()
						.Where(s => s.Symbol != null)
						.Where(s => s.Symbol.BaseType.Name == tuple.Item1 && s.Symbol.BaseType.ContainingNamespace.ToDisplayString() == tuple.Item2)
						.Select(t => ToDisplayString(t.ImplementedFor));
					output += $"{name} ({matchingPlatforms.JoinBy(@"/")}) ";
				}

				return output;
			}

			string GetLinkTarget((string symbolName, string symbolNamespace) tuple)
			{
				if (_kosherFrameworkViews.Contains(tuple))
				{
					return $"../{GetImplementedMembersFilename($"{tuple.symbolNamespace}.{tuple.symbolName}")}";
				}
				else
				{
					return null;
				}
			}

			IEnumerable<(INamedTypeSymbol Symbol, ImplementedFor ImplementedFor)> AllSymbols()
			{
				yield return (view.UAPSymbol, ImplementedFor.UAP);
				yield return (view.AndroidSymbol, ImplementedFor.Android);
				yield return (view.IOSSymbol, ImplementedFor.iOS);
				yield return (view.WasmSymbol, ImplementedFor.WASM);
				yield return (view.MacOSSymbol, ImplementedFor.MacOS);
			}
		}

		private static IGrouping<INamespaceSymbol, PlatformSymbols<INamedTypeSymbol>>[] GroupByNamespace(IEnumerable<PlatformSymbols<INamedTypeSymbol>> types)
		{
			return types.GroupBy(t => t.UAPSymbol.ContainingNamespace)
				.OrderBy(g => g.Key.ToDisplayString())
				.ToArray();
		}

		private static string GetImplementedMembersFilename(INamedTypeSymbol typeSymbol)
		{
			return GetImplementedMembersFilename(typeSymbol.ToDisplayString());
		}

		private static string GetImplementedMembersFilename(string typeString)
		{
			return $"{ImplementedPath}{typeString.ToLowerInvariant().Replace('.', '-')}.md";
		}

		private static SymbolDisplayFormat DisplayFormat { get; } = new SymbolDisplayFormat(SymbolDisplayGlobalNamespaceStyle.Omitted, SymbolDisplayTypeQualificationStyle.NameAndContainingTypes, SymbolDisplayGenericsOptions.IncludeTypeParameters, SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType, SymbolDisplayDelegateStyle.NameAndSignature, SymbolDisplayExtensionMethodStyle.Default, SymbolDisplayParameterOptions.IncludeType, SymbolDisplayPropertyStyle.NameOnly, SymbolDisplayLocalOptions.None, SymbolDisplayKindOptions.None, SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

		private static string ToDisplayString(ImplementedFor implementedFor)
		{
			switch (implementedFor)
			{
				case ImplementedFor.Main:
					return "all platforms";
				case ImplementedFor.Mobile:
					return "Android, iOS";
				case ImplementedFor.Xamarin:
					return "Android, iOS, MacOS";
				case ImplementedFor.UAP:
					return "UWP";
				case (ImplementedFor.Mobile | ImplementedFor.WASM):
					return "Android, iOS, WASM";
				default:
					return implementedFor.ToString();
			}
		}

		private static string GetCustomDocLink(string shortTypeName)
		{
			var customDoc = CustomDocMapping.FirstOrDefault(kvp => kvp.Value.Contains(shortTypeName)).Key;
			return customDoc != null ?
				$"../{customDoc}" :
				null;
		}

		private static readonly Dictionary<string, string[]> CustomDocMapping = new Dictionary<string, string[]>
		{
			["controls/ListViewBase.md"] = new[] { "ListView", "GridView", "ListViewBase", "ItemsStackPanel", "ItemsWrapGrid" },
			["controls/map-control-support.md"] = new[] { "MapControl" },
			["controls/MediaPlayerElement.md"] = new[] { "MediaPlayerElement", "MediaPlayerPresenter" },
			["controls/Pivot.md"] = new[] { "Pivot", "PivotHeaderItem", "PivotHeaderPanel" },
			["controls/ToggleSwitch.md"] = new[] { "ToggleSwitch" },
			["controls/working-with-commandbar.md"] = new[] { "CommandBar" },
			["features/shapes-and-brushes.md"] = new[] { "Ellipse", "Line", "Path", "Polygon", "Polyline", "Rectangle", "ArbitraryShapeBase" },
		};
	}
}
