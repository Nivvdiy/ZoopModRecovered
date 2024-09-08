using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

		private Dictionary<Type, ZoopComponentFilter> componentFilters = new Dictionary<Type, ZoopComponentFilter>();
		
		private ZoopComponentFilterRules() {
		}

		public void SetComponentFilter(Type componentType, FilterType filterType, List<string> memberNames) {
			if(componentFilters.ContainsKey(componentType)) {
				componentFilters[componentType].FilterType = filterType;
				componentFilters[componentType].MemberNames = memberNames;
			} else {
				componentFilters.Add(componentType, new ZoopComponentFilter {
					FilterType = filterType,
					MemberNames = memberNames
				});
			}
		}

		public ZoopComponentFilter GetComponentFilter(Type componentType) {
			if(componentFilters.TryGetValue(componentType, out ZoopComponentFilter filter)) {
				return filter;
			} else {
				return new ZoopComponentFilter {
					FilterType = FilterType.Blacklist,
					MemberNames = new List<String>()
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

				// Get all members (fields and properties) of the current type
				var members = currentType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList()
					.Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property)
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

			return combinedWhitelist;
		}

		public class ZoopComponentFilter {
			public FilterType FilterType { get; set; }
			public List<string> MemberNames { get; set; }
		}
	}

	public class FilterSetup {
		public static void ConfigureFilters() {
			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(UIAudioComponent),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(ScrollRect),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Transform),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
					"parent",
					"parentInternal"
				}
			);

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Mask),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(RectMask2D),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(GridLayoutGroup),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(ContentSizeFitter),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(ToggleGroup),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Toggle),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
					"onValueChanged",
					"m_Group",
					"isOn"
				}
			);

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Scrollbar),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
					"onValueChanged"
				}
			);

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(RectTransform),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(CanvasRenderer),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(UnityEngine.Object),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
					"name"
				}
			);

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Component),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Graphic),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(MaskableGraphic),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
					"m_ParentMask"
				}
			);

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Image),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(MonoBehaviour),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/

			/*ZoopComponentFilterRules.Instance.SetComponentFilter(
				typeof(Behaviour),
				ZoopComponentFilterRules.FilterType.Blacklist,
				new List<string> {
				}
			);*/
		}

	}
}
