using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Linq;
#endif
using Rive.Utils;

using UnityEngine;

namespace Rive
{


#if UNITY_EDITOR
    /// <summary>
    /// Read-only metadata describing the contents of an imported Rive file.
    /// </summary>
    [Serializable]
    public sealed class FileMetadata
    {
        private const int CURRENT_VERSION = 2;      // Increment this whenever the structure changes
        [SerializeField] private int m_Version = 2; //And also update this so we can check for changes in the future

        /// <summary>
        /// Metadata describing a state machine input.
        /// </summary>
        [Serializable]
        public sealed class InputMetadata
        {
            [SerializeField] private string m_Name;
            [SerializeField] private string m_Type;

            /// <summary>
            /// The input name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The Rive input type.
            /// </summary>
            public string Type { get { return m_Type; } }

            internal InputMetadata(string name, string type)
            {
                m_Name = name;
                m_Type = type;
            }
        }

        /// <summary>
        /// Metadata describing a state machine.
        /// </summary>
        [Serializable]
        public sealed class StateMachineMetadata
        {
            [SerializeField] private string m_Name;
            [SerializeField] private List<InputMetadata> m_Inputs = new List<InputMetadata>();

            /// <summary>
            /// The state machine name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The state machine inputs.
            /// </summary>
            public IReadOnlyList<InputMetadata> Inputs { get { return m_Inputs; } }

            internal StateMachineMetadata(string name)
            {
                m_Name = name;
            }

            internal void AddInput(InputMetadata input)
            {
                m_Inputs.Add(input);
            }
        }

        /// <summary>
        /// Metadata describing a view model property.
        /// </summary>
        [Serializable]
        public sealed class ViewModelPropertyMetadata
        {
            [SerializeField] private string m_Name;
            [SerializeField] private ViewModelDataType m_Type;
            [SerializeField] private string m_NestedViewModelName;
            [SerializeField] private string m_EnumTypeName;

            /// <summary>
            /// The property name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The Rive property type.
            /// </summary>
            public ViewModelDataType Type { get { return m_Type; } }

            /// <summary>
            /// The name of the nested view model if the property is a view model.
            /// </summary>
            public string NestedViewModelName { get { return m_NestedViewModelName; } }

            /// <summary>
            /// The name of the enum type if the property is an enum.
            /// </summary>
            public string EnumTypeName { get { return m_EnumTypeName; } }

            private ViewModelPropertyMetadata(string name, ViewModelDataType type, string nestedViewModelName = null, string enumTypeName = null)
            {
                m_Name = name;
                m_Type = type;
                m_NestedViewModelName = nestedViewModelName;
                m_EnumTypeName = enumTypeName;
            }

            internal static ViewModelPropertyMetadata FromPropertyData(ViewModelPropertyData propertyData, ViewModel viewModel)
            {
                if (propertyData.Type == ViewModelDataType.ViewModel)
                {
                    // Get the view model name by loading a view model instance and getting the name of the view model
                    var instance = viewModel.CreateInstance();

                    ViewModelInstance nestedInstance = instance.GetProperty<ViewModelInstance>(propertyData.Name);

                    if (nestedInstance == null)
                    {
                        DebugLogger.Instance.LogWarning("Could not find nested view model instance for property " + propertyData.Name);
                        return new ViewModelPropertyMetadata(propertyData.Name, propertyData.Type);
                    }

                    string nestedViewModelName = nestedInstance.ViewModelName;

                    nestedInstance.Dispose(); // We don't need the instance anymore

                    return new ViewModelPropertyMetadata(propertyData.Name, propertyData.Type, nestedViewModelName);
                }
                else if (propertyData.Type == ViewModelDataType.Enum)
                {
                    // Get the enum name by loading a view model instance and getting the associated enum type.
                    // Unfortunately, we need to do it this way because the native API doesn't expose the enum type name directly from the property data.
                    var instance = viewModel.CreateInstance();


                    var enumType = ViewModelInstancePropertyHandlersFactory.GetEnumForPropertyAtPath(instance, propertyData.Name);

                    instance.Dispose(); // We don't need the instance anymore

                    return new ViewModelPropertyMetadata(propertyData.Name, propertyData.Type, enumTypeName: enumType?.Name);
                }

                return new ViewModelPropertyMetadata(propertyData.Name, propertyData.Type);
            }
        }

        /// <summary>
        /// Metadata describing an artboard.
        /// </summary>
        [Serializable]
        public sealed class ArtboardMetadata
        {

            [SerializeField] private string m_Name;
            [SerializeField] private float m_Width;
            [SerializeField] private float m_Height;
            [SerializeField] private List<StateMachineMetadata> m_StateMachines = new List<StateMachineMetadata>();
            [SerializeField] private ViewModelMetadata m_DefaultViewModel;

            /// <summary>
            /// The artboard name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The authored artboard width.
            /// </summary>
            public float Width { get { return m_Width; } }

            /// <summary>
            /// The authored artboard height.
            /// </summary>
            public float Height { get { return m_Height; } }

            /// <summary>
            /// The state machines contained by the artboard.
            /// </summary>
            public IReadOnlyList<StateMachineMetadata> StateMachines { get { return m_StateMachines; } }

            /// <summary>
            /// The artboard's default view model, or <see langword="null"/> when none is configured.
            /// </summary>
            public ViewModelMetadata DefaultViewModel { get { return m_DefaultViewModel; } }

            internal ArtboardMetadata(string name, float width, float height, ViewModelMetadata defaultViewModel)
            {
                m_Name = name;
                m_Width = width;
                m_Height = height;
                m_DefaultViewModel = defaultViewModel;
            }

            internal void AddStateMachine(StateMachineMetadata stateMachine)
            {
                m_StateMachines.Add(stateMachine);
            }
        }

        /// <summary>
        /// Metadata describing a view model.
        /// </summary>
        [Serializable]
        public sealed class ViewModelMetadata
        {
            [SerializeField] private string m_Name;
            [SerializeField] private List<ViewModelPropertyMetadata> m_Properties = new List<ViewModelPropertyMetadata>();
            [SerializeField] private List<string> m_InstanceNames = new List<string>();

            /// <summary>
            /// The view model name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The properties declared by the view model.
            /// </summary>
            public IReadOnlyList<ViewModelPropertyMetadata> Properties { get { return m_Properties; } }

            /// <summary>
            /// The names of the view model instances authored in the Rive file.
            /// </summary>
            public IReadOnlyList<string> InstanceNames { get { return m_InstanceNames; } }

            private ViewModelMetadata(string name)
            {
                m_Name = name;
            }

            internal static ViewModelMetadata FromViewModel(ViewModel viewModel)
            {
                var viewModelMeta = new ViewModelMetadata(viewModel.Name);

                IReadOnlyList<ViewModelPropertyData> properties = viewModel.Properties;

                foreach (var property in properties)
                {
                    viewModelMeta.m_Properties.Add(ViewModelPropertyMetadata.FromPropertyData(property, viewModel));
                }

                viewModelMeta.m_InstanceNames.AddRange(viewModel.InstanceNames);

                return viewModelMeta;
            }
        }

        /// <summary>
        /// Metadata describing a Rive enum.
        /// </summary>
        [Serializable]
        public sealed class ViewModelEnumMetadata
        {
            [SerializeField] private string m_Name;
            [SerializeField] private string[] m_Values;

            /// <summary>
            /// The enum name authored in Rive.
            /// </summary>
            public string Name { get { return m_Name; } }

            /// <summary>
            /// The enum values in authored order.
            /// </summary>
            public IReadOnlyList<string> Values { get { return m_Values; } }

            internal ViewModelEnumMetadata(string name, IReadOnlyList<string> values)
            {
                m_Name = name;
                m_Values = values.ToArray();
            }
        }

        [SerializeField] private List<ArtboardMetadata> m_Artboards = new List<ArtboardMetadata>();
        [SerializeField] private List<ViewModelMetadata> m_ViewModels = new List<ViewModelMetadata>();
        [SerializeField] private List<ViewModelEnumMetadata> m_Enums = new List<ViewModelEnumMetadata>();

        /// <summary>
        /// The artboards contained by the Rive file.
        /// </summary>
        public IReadOnlyList<ArtboardMetadata> Artboards { get { return m_Artboards; } }

        /// <summary>
        /// The view models contained by the Rive file.
        /// </summary>
        public IReadOnlyList<ViewModelMetadata> ViewModels { get { return m_ViewModels; } }

        /// <summary>
        /// The enums contained by the Rive file.
        /// </summary>
        public IReadOnlyList<ViewModelEnumMetadata> Enums { get { return m_Enums; } }

        internal void AddArtboard(ArtboardMetadata artboard)
        {
            m_Artboards.Add(artboard);
        }

        internal void AddViewModel(ViewModelMetadata viewModel)
        {
            m_ViewModels.Add(viewModel);
        }

        internal void AddEnum(ViewModelEnumMetadata enumMetadata)
        {
            m_Enums.Add(enumMetadata);
        }

        internal string[] GetArtboardNames()
        {
            return Artboards.Select(a => a.Name).ToArray();
        }

        internal string[] GetStateMachineNames(string artboardName)
        {
            var artboard = Artboards.FirstOrDefault(a => a.Name == artboardName);
            return artboard?.StateMachines.Select(sm => sm.Name).ToArray() ?? new string[0];
        }

        internal ArtboardMetadata GetArtboard(string name)
        {
            return Artboards.FirstOrDefault(a => a.Name == name);
        }

        internal bool NeedsReload()
        {
            // Force reload for unversioned (0) or outdated data
            return m_Version != CURRENT_VERSION;
        }
    }
#endif


    /// <summary>
    /// Represents a Rive asset (.riv)
    /// </summary>
    public class Asset : ScriptableObject
    {
        [HideInInspector]
        [SerializeField]
        private byte[] m_Bytes;

        /// <summary>
        /// The raw bytes of the Rive asset
        /// </summary>
        public byte[] Bytes { get { return m_Bytes; } }

        [HideInInspector]
        [SerializeField]
        private EmbeddedAssetData[] m_EmbeddedAssets;

        /// <summary>
        /// An array of all the embedded asset data in this Rive asset
        /// </summary>
        public IReadOnlyList<EmbeddedAssetData> EmbeddedAssets { get { return m_EmbeddedAssets; } }

        /// <summary>
        /// The number of embedded asset data in this Rive asset
        /// </summary>
        public int EmbeddedAssetCount { get { return m_EmbeddedAssets == null ? 0 : m_EmbeddedAssets.Length; } }

#if UNITY_EDITOR
        [SerializeField]
        private FileMetadata m_FileMetadata;

        /// <summary>
        /// Read-only metadata about the contents of the Rive file. Available only in the Unity editor.
        /// </summary>
        public FileMetadata EditorOnlyMetadata
        {
            get
            {
                if (m_FileMetadata == null || m_FileMetadata.NeedsReload())
                {
                    GenerateFileMetadata();
                }
                return m_FileMetadata;
            }
        }

        private void GenerateFileMetadata()
        {
            m_FileMetadata = new FileMetadata();

            using (var file = File.Load(this))
            {
                if (file == null) return;

                // View models

                IReadOnlyList<ViewModel> viewModels = file.ViewModels;

                foreach (var viewModel in viewModels)
                {
                    var viewModelMeta = FileMetadata.ViewModelMetadata.FromViewModel(viewModel);

                    m_FileMetadata.AddViewModel(viewModelMeta);
                }

                // Enums

                foreach (var enumData in file.ViewModelEnums)
                {
                    var enumMeta = new FileMetadata.ViewModelEnumMetadata(enumData.Name, enumData.Values);
                    m_FileMetadata.AddEnum(enumMeta);
                }


                for (uint i = 0; i < file.ArtboardCount; i++)
                {
                    var artboard = file.Artboard(i);
                    if (artboard == null) continue;

                    FileMetadata.ViewModelMetadata defaultViewModel = null;

                    if (artboard.DefaultViewModel != null)
                    {
                        defaultViewModel = FileMetadata.ViewModelMetadata.FromViewModel(artboard.DefaultViewModel);

                    }

                    var artboardMeta = new FileMetadata.ArtboardMetadata(name: file.ArtboardName(i), width: artboard.Width, height: artboard.Height, defaultViewModel: defaultViewModel);


                    for (uint j = 0; j < artboard.StateMachineCount; j++)
                    {
                        var stateMachine = artboard.StateMachine(j);
                        if (stateMachine == null) continue;

                        var smMeta = new FileMetadata.StateMachineMetadata(artboard.StateMachineName(j));

                        foreach (var input in stateMachine.Inputs())
                        {
                            var inputType = input.IsBoolean ? "Boolean" :
                                            input.IsNumber ? "Number" :
                                            input.IsTrigger ? "Trigger" : "Unknown";
                            smMeta.AddInput(new FileMetadata.InputMetadata(input.Name, inputType));
                        }


                        artboardMeta.AddStateMachine(smMeta);
                    }

                    m_FileMetadata.AddArtboard(artboardMeta);
                }
            }
        }
#endif

        /// <summary>
        /// Initializes the asset with the given bytes and embedded asset information.
        /// </summary>
        /// <param name="bytes"> The raw bytes of the Rive asset. </param>
        /// <param name="embeddedAssetsData"> The embedded asset data in the Rive asset. </param>
        internal void SetData(byte[] bytes, EmbeddedAssetData[] embeddedAssetsData)
        {
            m_Bytes = bytes;
            m_EmbeddedAssets = embeddedAssetsData;

#if UNITY_EDITOR
            GenerateFileMetadata();
#endif
        }

        /// <summary>
        /// Create a new Rive asset instance from the given bytes and embedded asset data.
        /// </summary>
        /// <param name="bytes"> The raw bytes of the Rive asset. </param>
        /// <param name="embeddedAssetsData"> The embedded asset data in the Rive asset. </param>
        /// <returns> The created Rive asset instance. </returns>
        public static Asset Create(byte[] bytes, EmbeddedAssetData[] embeddedAssetsData)
        {
            var asset = ScriptableObject.CreateInstance<Asset>();
            asset.SetData(bytes, embeddedAssetsData);
            return asset;
        }

    }


}