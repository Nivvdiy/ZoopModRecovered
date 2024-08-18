using Assets.Scripts.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZoopMod.Zoop.SettingsMenu {
	public class ZoopComponentFilterRules {

		private static ZoopComponentFilterRules _instance;
		private static readonly object instanceLock = new object();
		public static ZoopComponentFilterRules Instance {
			get {
				if(_instance == null) {
					lock(instanceLock) {
						if(_instance == null) {
							_instance = new ZoopComponentFilterRules();
							FilterSetup.ConfigureFilters();
						}
					}
				}
				return _instance;
			}
		}

		public enum FilterType {
			Whitelist,
			Blacklist
		}

		private Dictionary<System.Type, ZoopComponentFilter> componentFilters = new Dictionary<System.Type, ZoopComponentFilter>();

		private ZoopComponentFilterRules() {
		}

		public void SetComponentFilter(System.Type componentType, FilterType filterType, List<string> memberNames) {
			if(componentFilters.ContainsKey(componentType)) {
				componentFilters[componentType].FilterType = filterType;
				componentFilters[componentType].MemberNames = memberNames;
			} else {
				componentFilters.Add(componentType, new ZoopComponentFilter{
					FilterType = filterType,
					MemberNames = memberNames
				});
			}
		}

		public ZoopComponentFilter GetComponentFilter(System.Type componentType) {
			if(componentFilters.TryGetValue(componentType, out ZoopComponentFilter filter)) {
				return filter;
			} else {
				return new ZoopComponentFilter{
					FilterType = FilterType.Blacklist,
					MemberNames = new List<string>()
				};
			}
		}

		// This method builds a combined whitelist based on the inheritance hierarchy
		public List<string> BuildConsolidatedWhitelist(System.Type type) {
			List<string> combinedWhitelist = new List<string>();
			HashSet<string> excludedFromBlacklist = new HashSet<string>();
			HashSet<string> tempIncludedFromBlacklist = new HashSet<string>();

			System.Type currentType = type;

			while(currentType != null) {
				ZoopComponentFilter filter = GetComponentFilter(currentType);

				// Get all writable members (fields and properties) of the current type
				List<string> members = currentType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
						.Where(member => member.MemberType == MemberTypes.Field && !((FieldInfo)member).IsInitOnly ||
										 member.MemberType == MemberTypes.Property && ((PropertyInfo)member).CanWrite)
						.Select(member => member.Name)
						.ToList();

				if(filter.FilterType == FilterType.Whitelist) {
					// Add whitelist members if they are not excluded by a lower-level blacklist
					foreach(string member in filter.MemberNames) {
						if(!excludedFromBlacklist.Contains(member)) {
							combinedWhitelist.Add(member);
						}
					}
				} else if(filter.FilterType == FilterType.Blacklist) {
					// Mark members in blacklist to be excluded, and add the remaining to the whitelist
					foreach(string member in filter.MemberNames) {
						excludedFromBlacklist.Add(member);
					}

					// For a blacklist, include everything that is not explicitly in the blacklist
					foreach(string member in members) {
						if(!excludedFromBlacklist.Contains(member)) {
							tempIncludedFromBlacklist.Add(member); // Temporarily include
						}
					}
				}

				currentType = currentType.BaseType;
			}

			// Combine the explicitly whitelisted members with the members included by default in blacklists
			combinedWhitelist.AddRange(tempIncludedFromBlacklist);

			// Remove duplicates and ensure the final whitelist is unique
			combinedWhitelist = combinedWhitelist.Distinct().ToList();

			return combinedWhitelist;
		}

		public class ZoopComponentFilter {
			public FilterType FilterType { get; set; }
			public List<string> MemberNames { get; set; }
		}
	}

	public class FilterSetup {
		public static void ConfigureFilters() {

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Transform),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"parent",
					"parentInternal",
					"position",
					"rotation",
					"eulerAngles",
					"scale",
					"worldToLocalMatrix",
					"root"
				}
			);
			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(RectTransform),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(LocalizedText),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"TextMesh",
					"StringKey"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Toggle),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"onValueChanged",
					"m_OnValueChanged",
					"m_Group",
					"isOn",
					"graphic",
					"image",
					"targetGraphic"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Slider),
				ZoopComponentFilterRules.FilterType.Whitelist,
				new List<string>{
					"direction"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(SetSliderValueTMP),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"TargetField",
					"TargetSlider"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Selectable),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"image",
					"targetGraphic"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Scrollbar),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"onValueChanged",
					"m_OnValueChanged",
					"handleRect",
					"m_HandleRect",
					"m_ContainerRect"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(ScrollRect),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"onValueChanged",
					"m_OnValueChanged",
					"horizontalScrollbar",
					"m_HorizontalScrollbar",
					"m_HorizontalScrollbarRect",
					"verticalScrollbar",
					"m_VerticalScrollbar",
					"m_VerticalScrollbarRect",
					"content",
					"m_Content",
					"viewport",
					"m_Viewport",
					"viewRect",
					"m_ViewRect",
					"m_ContentBounds",
					"m_ViewBounds",
					"m_PrevContentBounds",
					"m_PrevViewBounds",
					"m_Rect"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Object),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"name",
					"onValueChanged",
					"m_OnValueChanged"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(TMP_InputField),
				ZoopComponentFilterRules.FilterType.Whitelist,
				new List<string>{
					"asteriskChar",
					"caretBlinkRate",
					"caretColor",
					"caretWidth",
					"characterLimit",
					"characterValidation",
					"customCaretColor",
					"flexibleHeight",
					"flexibleWidth",
					"fontAsset",
					"inputType",
					"inputValidator",
					"isRichTextEditingAllowed",
					"keepTextSelectionVisible",
					"layoutPriority",
					"lineLimit",
					"lineType",
					"minHeight",
					"minWidth",
					"multiLine"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(TextMeshProUGUI),
				ZoopComponentFilterRules.FilterType.Whitelist,
				new List<string>{
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(TMP_InputField),
				ZoopComponentFilterRules.FilterType.Whitelist,
				new List<string>{
					"caretBlinkRate",
					"caretWidth",
					"scrollSensitivity",
					"caretColor",
					"customCaretColor",
					"selectionColor",
					"characterLimit",
					"pointSize",
					"fontAsset",
					"onFocusSelectAll",
					"resetOnDeActivation",
					"restoreOriginalTextOnEscape",
					"isRichTextEditingAllowed",
					"contentType",
					"lineType",
					"lineLimit",
					"inputType",
					"keyboardType",
					"characterValidation",
					"inputValidator",
					"richText",
					"multiLine",
					"asteriskChar"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(TMP_Text),
				ZoopComponentFilterRules.FilterType.Whitelist,
				new List<string>{
					"alignment",
					"characterSpacing",
					"color",
					"enableAutoSizing",
					"enableWordWrapping",
					"extraPadding",
					"font",
					"fontMaterial",
					"fontSize",
					"fontSizeMax",
					"fontSizeMin",
					"fontStyle",
					"fontWeight",
					"horizontalAlignment",
					"verticalAlignment",
					"overflowMode"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(MaskableGraphic),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"m_ParentMask"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(SettingItem),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string>{
					"Selectable",
					"SettingType"
				}
			);
		}

	}
}