﻿namespace LinqPadSpy.Controls
{
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Windows.Controls;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls.Primitives;

    using ICSharpCode.Decompiler;
    using ICSharpCode.ILSpy;
    using ICSharpCode.ILSpy.TextView;
    using ICSharpCode.ILSpy.TreeNodes;
    using ICSharpCode.TreeView;

    using Mono.Cecil;

    /// <summary>
    /// Interaction logic for LinqPadSpyContainer.xaml
    /// </summary>
    public partial class LinqPadSpyContainer : UserControl, ISpyWindow
    {
        static DecompilationOptions Options
        {
            get
            {
                return new DecompilationOptions()
                {
                    DecompilerSettings = new DecompilerSettings()
                    {
                        // False = Show compiler generated code
                        AlwaysGenerateExceptionVariableForCatchBlocks = true,
                        AnonymousMethods = false,
                        AsyncAwait = false,
                        AutomaticEvents = true,
                        AutomaticProperties = false,
                        ExpressionTrees = true,
                        ForEachStatement = false,
                        FullyQualifyAmbiguousTypeNames = true,
                        IntroduceIncrementAndDecrement = true,
                        LockStatement = true,
                        MakeAssignmentExpressions = true,
                        UseDebugSymbols = true,
                        YieldReturn = false,
                        QueryExpressions = true,
                        SwitchStatementOnString = false
                    }
                };
            }
        }

        public event NotifyCollectionChangedEventHandler CurrentAssemblyListChanged;

        public AssemblyList CurrentAssemblyList { get; private set; }

        readonly Language decompiledLanguage;

        readonly Application currentApplication;

        AssemblyListTreeNode assemblyListTreeNode;

        readonly DecompilerTextView decompilerTextView;

        readonly string assemblyPath;

        readonly ILSpySettings spySettings;

        readonly SessionSettings sessionSettings;

        public LinqPadSpyContainer(Application currentApplication, Language decompiledLanguage)
        {
            if (currentApplication == null)
            {
                throw new ArgumentNullException("currentApplication");
            }
            if (decompiledLanguage == null)
            {
                throw new ArgumentNullException("decompiledLanguage");
            }

            this.decompiledLanguage = decompiledLanguage;

            this.currentApplication = currentApplication;

            // Initialize supported ILSpy languages. Values used for the combobox.
            Languages.Initialize(CompositionContainerBuilder.Container);

            this.CurrentAssemblyList = new AssemblyList("LINQPadAssemblyList", this.currentApplication);

            // A hack to get around the global shared state of the Window object throughout ILSpy.
            ICSharpCode.ILSpy.MainWindow.SpyWindow = this; 

            this.spySettings = ILSpySettings.Load();
            
            this.sessionSettings = new SessionSettings(this.spySettings)
            {
                ActiveAssemblyList = this.CurrentAssemblyList.ListName
            };

            SetUpDataContext();

            this.assemblyPath = LinqPadUtil.GetLastLinqPadQueryAssembly(); 

            this.decompilerTextView = GetDecompilerTextView();

            InitializeComponent();

            this.mainPane.Content = this.decompilerTextView;

            this.Loaded += new RoutedEventHandler(this.MainWindowLoaded);
        }

        void MainWindowLoaded(object sender, RoutedEventArgs e)
		{		
			ShowAssemblyList();
		}

        void ShowAssemblyList()
        {
            //history.Clear();
            //this.assemblyList = assemblyList;

            CurrentAssemblyList.assemblies.CollectionChanged += assemblyList_Assemblies_CollectionChanged;

            assemblyListTreeNode = new AssemblyListTreeNode(this.CurrentAssemblyList);
            assemblyListTreeNode.FilterSettings = this.sessionSettings.FilterSettings.Clone();
            assemblyListTreeNode.Select = SelectNode;

            treeView.Root = assemblyListTreeNode;
        }

        void assemblyList_Assemblies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                //history.RemoveAll(_ => true);
            }
            if (e.OldItems != null)
            {
                var oldAssemblies = new HashSet<LoadedAssembly>(e.OldItems.Cast<LoadedAssembly>());
                
                //history.RemoveAll(n => n.TreeNodes.Any(
                //    nd => nd.AncestorsAndSelf().OfType<AssemblyTreeNode>().Any(
                //        a => oldAssemblies.Contains(a.LoadedAssembly))));
            }
            if (CurrentAssemblyListChanged != null)
                CurrentAssemblyListChanged(this, e);
        }

        internal void SelectNode(SharpTreeNode obj)
		{
			if (obj != null) {
				if (!obj.AncestorsAndSelf().Any(node => node.IsHidden)) {
					// Set both the selection and focus to ensure that keyboard navigation works as expected.
					treeView.FocusNode(obj);
					treeView.SelectedItem = obj;
				} else {
					MessageBox.Show("Navigation failed because the target is hidden or a compiler-generated class.\n" +
						"Please disable all filters that might hide the item (i.e. activate " +
						"\"View > Show internal types and members\") and try again.",
						"ILSpy", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
		}

        void SetUpDataContext()
        {            
			sessionSettings.FilterSettings.PropertyChanged += filterSettings_PropertyChanged;

            this.DataContext = sessionSettings;
        }

        void filterSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "Language")
			{
			    var selectedLanguage = ((FilterSettings)sender).Language;

				DecompileSelectedNodes(selectedLanguage);
			}
		}

        void DecompileSelectedNodes(Language language, DecompilerTextViewState state = null, bool recordHistory = true)
		{	
            //if (recordHistory) {
            //    var dtState = decompilerTextView.GetState();
            //    if(dtState != null)
            //        history.UpdateCurrent(new NavigationState(dtState));
            //    history.Record(new NavigationState(treeView.SelectedItems.OfType<SharpTreeNode>()));
            //}
			
			if (treeView.SelectedItems.Count == 1) {
				ILSpyTreeNode node = treeView.SelectedItem as ILSpyTreeNode;
				if (node != null && node.View(decompilerTextView))
					return;
			}

            var typesToDecompile = GetModuleTypes();

            //Options.TextViewState = state

			decompilerTextView.Decompile(language, typesToDecompile, Options);
		}

        DecompilerTextView GetDecompilerTextView()
        {
            var typesToDecompile = GetModuleTypes();

            var decomTextView = new DecompilerTextView();

            CompositionContainerBuilder.Container.ComposeParts(decomTextView);

            decomTextView.Decompile(this.decompiledLanguage, typesToDecompile, Options);

            return decomTextView;
        }

        LoadedAssembly LoadAssembly()
        {
            return CurrentAssemblyList.OpenAssembly(this.assemblyPath);
        }

        AssemblyDefinition GetAssemblyDefinition(LoadedAssembly loadedAssembly)
        {
            var asmDef = loadedAssembly.AssemblyDefinition;

            if (asmDef == null)
            {
                throw new InvalidOperationException("Could not load for some reason.");
            }

            return asmDef;
        }

        IEnumerable<TypeTreeNode> GetModuleTypes()
        {
            // Load assembly metadata
            var loadedAssembly = LoadAssembly();

            var assemblyDefinition = GetAssemblyDefinition(loadedAssembly);

            var mainModule = assemblyDefinition.MainModule;

            var assemblyTreeNode = new AssemblyTreeNode(loadedAssembly);

            return mainModule.Types.OrderBy(t => t.FullName).Select(type => new TypeTreeNode(type, assemblyTreeNode));
        }
        
        void Thumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            sessionSettings.SplitterPosition = leftColumn.Width.Value / (leftColumn.Width.Value + rightColumn.Width.Value);
            this.sessionSettings.Save();
        }
    }
}
