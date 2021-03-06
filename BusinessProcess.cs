﻿using Org.Reddragonit.BpmEngine.Attributes;
using Org.Reddragonit.BpmEngine.Elements;
using Org.Reddragonit.BpmEngine.Elements.Collaborations;
using Org.Reddragonit.BpmEngine.Elements.Processes;
using Org.Reddragonit.BpmEngine.Elements.Processes.Events;
using Org.Reddragonit.BpmEngine.Elements.Processes.Gateways;
using Org.Reddragonit.BpmEngine.Elements.Processes.Tasks;
using Org.Reddragonit.BpmEngine.Interfaces;
using Org.Reddragonit.BpmEngine.State;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;

namespace Org.Reddragonit.BpmEngine
{
    public sealed class BusinessProcess
    {
        private static readonly TimeSpan _ANIMATION_DELAY = new TimeSpan(0,0,1);
        private const int _DEFAULT_PADDING = 100;
        private const int _VARIABLE_NAME_WIDTH = 200;
        private const int _VARIABLE_VALUE_WIDTH = 300;
        private const int _VARIABLE_IMAGE_WIDTH = _VARIABLE_NAME_WIDTH+_VARIABLE_VALUE_WIDTH;

        private bool _isSuspended = false;
        private ManualResetEvent _mreSuspend;
        private List<object> _components;
        private Dictionary<string, IElement> _elements;

        private IElement _GetElement(string id)
        {
            if (_elements.ContainsKey(id))
                return _elements[id];
            return null;
        }
        private IElement[] _Elements
        {
            get
            {
                List<IElement> ret = new List<IElement>();
                foreach (object obj in _components)
                {
                    if (new List<Type>(obj.GetType().GetInterfaces()).Contains(typeof(IElement)))
                        ret.Add((IElement)obj);
                }
                return ret.ToArray();
            }
        }

        private void _RecurAddChildren(IElement parent)
        {
            _elements.Add(parent.id,parent);
            if (parent is IParentElement)
            {
                foreach (IElement elem in ((IParentElement)parent).Children)
                    _RecurAddChildren(elem);
            }
        }

        private XmlDocument _doc;
        public XmlDocument Document { get { return _doc; } }

        private ProcessState _state;
        public ProcessState State { get { return _state; } }

        private LogLevels _stateLogLevel = LogLevels.None;
        public LogLevels StateLogLevel { get { return _stateLogLevel; } set { _stateLogLevel = value; } }

        private ManualResetEvent _processLock;

        private sProcessRuntimeConstant[] _constants;
        public object this[string name]
        {
            get
            {
                if (_constants != null)
                {
                    foreach (sProcessRuntimeConstant sprc in _constants)
                    {
                        if (sprc.Name == name)
                            return sprc.Value;
                    }
                }
                if (_Elements != null)
                {
                    foreach (IElement elem in _Elements)
                    {
                        if (elem is Definition)
                        {
                            Definition def = (Definition)elem;
                            if (def.ExtensionElement != null)
                            {
                                foreach (IElement e in ((ExtensionElements)def.ExtensionElement).Children)
                                {
                                    if (e is DefinitionVariable)
                                    {
                                        DefinitionVariable dv = (DefinitionVariable)e;
                                        if (dv.Name == name)
                                            return dv.Value;
                                    }else if (e is DefinitionFile)
                                    {
                                        DefinitionFile df = (DefinitionFile)e;
                                        if (string.Format("{0}.{1}", df.Name, df.Extension) == name || df.Name == name)
                                            return new sFile(df.Name, df.Extension, df.ContentType, df.Content);
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                return null;
            }
        }

        internal string[] Keys
        {
            get
            {
                List<string> ret = new List<string>();
                if (_constants != null)
                {
                    foreach (sProcessRuntimeConstant sprc in _constants)
                    {
                        if (!ret.Contains(sprc.Name))
                            ret.Add(sprc.Name);
                    }
                }
                if (_Elements != null)
                {
                    foreach (IElement elem in _Elements)
                    {
                        if (elem is Definition)
                        {
                            Definition def = (Definition)elem;
                            if (def.ExtensionElement != null)
                            {
                                foreach (IElement e in ((ExtensionElements)def.ExtensionElement).Children)
                                {
                                    if (e is DefinitionVariable)
                                    {
                                        DefinitionVariable dv = (DefinitionVariable)e;
                                        if (!ret.Contains(dv.Name))
                                            ret.Add(dv.Name);
                                    }
                                    else if (e is DefinitionFile)
                                    {
                                        DefinitionFile df = (DefinitionFile)e;
                                        if (!ret.Contains(string.Format("{0}.{1}", df.Name, df.Extension)))
                                            ret.Add(string.Format("{0}.{1}", df.Name, df.Extension));
                                        if (!ret.Contains(df.Name))
                                            ret.Add(df.Name);
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                return ret.ToArray();
            }
        }

        [ThreadStatic()]
        private static BusinessProcess _current;
        public static BusinessProcess Current { get { return _current; } }

        [ThreadStatic()]
        private static ElementTypeCache _elementMapCache;
        internal static ElementTypeCache ElementMapCache { get { return _elementMapCache; } }

        #region delegates
        #region Ons
        private OnEventStarted _onEventStarted;
        public OnEventStarted OnEventStarted { get { return _onEventStarted; } set { _onEventStarted = value; } }

        private OnEventCompleted _onEventCompleted;
        public OnEventCompleted OnEventCompleted{get{return _onEventCompleted;}set{_onEventCompleted = value;}}

        private OnEventError _onEventError;
        public OnEventError OnEventError{get{return _onEventError;}set{_onEventError=value;}}

        private OnTaskStarted _onTaskStarted;
        public OnTaskStarted OnTaskStarted{get{return _onTaskStarted;}set{_onTaskStarted=value;}}

        private OnTaskCompleted _onTaskCompleted;
        public OnTaskCompleted OnTaskCompleted{get{return _onTaskCompleted;}set{_onTaskCompleted=value;}}
        
        private OnTaskError _onTaskError;
        public OnTaskError OnTaskError{get{return _onTaskError;}set{_onTaskError = value;}}

        private OnProcessStarted _onProcessStarted;
        public OnProcessStarted OnProcessStarted{get{return _onProcessStarted;}set{_onProcessStarted=value;}}
        
        private OnProcessCompleted _onProcessCompleted;
        public OnProcessCompleted OnProcessCompleted{get{return _onProcessCompleted;}set{_onProcessCompleted = value;}}

        private OnProcessError _onProcessError;
        public OnProcessError OnProcessError { get { return _onProcessError; } set { _onProcessError = value; } }

        private OnSequenceFlowCompleted _onSequenceFlowCompleted;
        public OnSequenceFlowCompleted OnSequenceFlowCompleted { get { return _onSequenceFlowCompleted; } set { _onSequenceFlowCompleted = value; } }

        private OnMessageFlowCompleted _onMessageFlowCompleted;
        public OnMessageFlowCompleted OnMessageFlowCompleted { get { return _onMessageFlowCompleted; } set { _onMessageFlowCompleted = value; } }

        private OnGatewayStarted _onGatewayStarted;
        public OnGatewayStarted OnGatewayStarted { get { return _onGatewayStarted; } set { _onGatewayStarted = value; } }

        private OnGatewayCompleted _onGatewayCompleted;
        public OnGatewayCompleted OnGatewayCompleted { get { return _onGatewayCompleted; } set { _onGatewayCompleted = value; } }

        private OnGatewayError _onGatewayError;
        public OnGatewayError OnGatewayError { get { return _onGatewayError; } set { _onGatewayError = value; } }

        private OnSubProcessStarted _onSubProcessStarted;
        public OnSubProcessStarted OnSubProcessStarted { get { return _onSubProcessStarted; } set { _onSubProcessStarted = value; } }

        private OnSubProcessCompleted _onSubProcessCompleted;
        public OnSubProcessCompleted OnSubProcessCompleted { get { return _onSubProcessCompleted; } set { _onSubProcessCompleted = value; } }

        private OnSubProcessError _onSubProcessError;
        public OnSubProcessError OnSubProcessError { get { return _onSubProcessError; } set { _onSubProcessError = value; } }

        public OnStateChange OnStateChange { set { _state.OnStateChange = value; } }
        #endregion

        #region Validations
        private static bool _DefaultEventStartValid(IElement Event, ProcessVariablesContainer variables){return true;}
        private IsEventStartValid _isEventStartValid = new IsEventStartValid(_DefaultEventStartValid);
        public IsEventStartValid IsEventStartValid { get { return _isEventStartValid; } set { _isEventStartValid = value; } }

        private static bool _DefaultProcessStartValid(IElement Event, ProcessVariablesContainer variables){return true;}
        private IsProcessStartValid _isProcessStartValid = new IsProcessStartValid(_DefaultProcessStartValid);
        public IsProcessStartValid IsProcessStartValid { get { return _isProcessStartValid; } set { _isProcessStartValid = value; } }
        #endregion

        #region Conditions
        private static bool _DefaultFlowValid(IElement flow, ProcessVariablesContainer variables) { return true; }
        private IsFlowValid _isFlowValid = new IsFlowValid(_DefaultFlowValid);
        public IsFlowValid IsFlowValid { get { return _isFlowValid; } set { _isFlowValid = value; } }
        #endregion

        #region Tasks
        private ProcessBusinessRuleTask _processBusinessRuleTask;
        public ProcessBusinessRuleTask ProcessBusinessRuleTask { get { return _processBusinessRuleTask; } set { _processBusinessRuleTask = value; } }

        private BeginManualTask _beginManualTask;
        public BeginManualTask BeginManualTask { get { return _beginManualTask; } set { _beginManualTask = value; } }

        private ProcessRecieveTask _processRecieveTask;
        public ProcessRecieveTask ProcessRecieveTask { get { return _processRecieveTask; } set { _processRecieveTask = value; } }

        private ProcessScriptTask _processScriptTask;
        public ProcessScriptTask ProcessScriptTask { get { return _processScriptTask; } set { _processScriptTask=value; } }

        private ProcessSendTask _processSendTask;
        public ProcessSendTask ProcessSendTask { get { return _processSendTask; } set { _processSendTask = value; } }

        private ProcessServiceTask _processServiceTask;
        public ProcessServiceTask ProcessServiceTask { get { return _processServiceTask; } set { _processServiceTask = value; } }

        private ProcessTask _processTask;
        public ProcessTask ProcessTask { get { return _processTask; } set { _processTask = value; } }

        private BeginUserTask _beginUserTask;
        public BeginUserTask BeginUserTask { get { return _beginUserTask; } set { _beginUserTask = value; } }

        #region TaskCallBacks
        private void _CompleteExternalTask(string taskID, ProcessVariablesContainer variables)
        {
            bool success = false;
            IElement elem = _GetElement(taskID);
            if (elem != null && elem is ATask)
            {
                _MergeVariables((ATask)elem, variables);
                success = true;
            }
            if (!success)
                throw new Exception(string.Format("Unable to locate task with id {0}", taskID));
        }

        private void _CompleteUserTask(string taskID, ProcessVariablesContainer variables,string completedByID)
        {
            bool success = false;
            IElement elem = _GetElement(taskID);
            if (elem != null && elem is ATask)
            {
                _MergeVariables((UserTask)elem, variables, completedByID);
                success = true;
            }
            if (!success)
                throw new Exception(string.Format("Unable to locate task with id {0}", taskID));
        }

        private void _ErrorExternalTask(string taskID, Exception ex)
        {
            bool success = false;
            IElement elem = _GetElement(taskID);
            if (elem != null && elem is ATask)
            {
                if (_onTaskError != null)
                    _onTaskError((ATask)elem, new ReadOnlyProcessVariablesContainer(elem.id, _state, this, ex));
                lock (_state)
                {
                    _state.Path.FailTask((ATask)elem, ex);
                }
                success = true;
            }
            if (!success)
                throw new Exception(string.Format("Unable to locate task with id {0}", taskID));
        }

        public void CompleteUserTask(string taskID, ProcessVariablesContainer variables,string completedByID)
        {
            _CompleteUserTask(taskID, variables,completedByID);
        }

        public void ErrorUserTask(string taskID, Exception ex)
        {
            _ErrorExternalTask(taskID, ex);
        }

        public void CompleteManualTask(string taskID, ProcessVariablesContainer variables)
        {
            _CompleteExternalTask(taskID, variables);
        }

        public void ErrorManualTask(string taskID, Exception ex)
        {
            _ErrorExternalTask(taskID, ex);
        }
        #endregion

        #endregion

        #region Logging
        private LogLine _logLine;
        public LogLine LogLine { get { return _logLine; }set { _logLine = value; } }

        private LogException _logException;
        public LogException LogException { get { return _logException; }set { _logException = value; } }
        #endregion

        #endregion

        private BusinessProcess() {
            _processLock = new ManualResetEvent(false);
            _mreSuspend = new ManualResetEvent(false);
        }

        public BusinessProcess(XmlDocument doc)
            :this(doc,LogLevels.None) { }

        public BusinessProcess(XmlDocument doc,LogLine logLine)
            : this(doc, LogLevels.None,logLine) { }

        public BusinessProcess(XmlDocument doc,sProcessRuntimeConstant[] constants)
            : this(doc, LogLevels.None,constants) { }

        public BusinessProcess(XmlDocument doc, sProcessRuntimeConstant[] constants,LogLine logLine)
            : this(doc, LogLevels.None, constants,logLine) { }

        public BusinessProcess(XmlDocument doc, LogLevels stateLogLevel)
            : this(doc, LogLevels.None, null,null) { }

        public BusinessProcess(XmlDocument doc, LogLevels stateLogLevel,LogLine logLine)
            : this(doc, LogLevels.None, null,logLine) { }

        public BusinessProcess(XmlDocument doc, LogLevels stateLogLevel, sProcessRuntimeConstant[] constants)
            : this(doc, stateLogLevel, constants, null) { }

        public BusinessProcess(XmlDocument doc, LogLevels stateLogLevel,sProcessRuntimeConstant[] constants,LogLine logLine)
        {
            _stateLogLevel = stateLogLevel;
            _constants = constants;
            _logLine = logLine;
            List<Exception> exceptions = new List<Exception>();
            _processLock = new ManualResetEvent(false);
            _mreSuspend = new ManualResetEvent(false);
            _doc = doc;
            _current = this;
            _elementMapCache = new BpmEngine.ElementTypeCache();
            DateTime start = DateTime.Now;
            WriteLogLine(LogLevels.Info,new StackFrame(1,true),DateTime.Now,"Producing new Business Process from XML Document");
            _components = new List<object>();
            _elements = new Dictionary<string, Interfaces.IElement>();
            XmlPrefixMap map = new XmlPrefixMap();
            foreach (XmlNode n in doc.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element)
                {
                    if (map.Load((XmlElement)n))
                        _elementMapCache.MapIdeals(map);
                    IElement elem = Utility.ConstructElementType((XmlElement)n, map,null);
                    if (elem != null)
                    {
                        _components.Add(elem);
                        _RecurAddChildren(elem);
                    }
                    else
                        _components.Add(n);
                }
                else
                    _components.Add(n);
            }
            if (_Elements.Length== 0)
                exceptions.Add(new XmlException("Unable to load a bussiness process from the supplied document.  No instance of bpmn:definitions was located."));
            else
            {
                bool found = false;
                foreach (IElement elem in _Elements)
                {
                    if (elem is Definition)
                        found = true;
                }
                if (!found)
                    exceptions.Add(new XmlException("Unable to load a bussiness process from the supplied document.  No instance of bpmn:definitions was located."));
            }
            if (exceptions.Count == 0)
            {
                foreach (IElement elem in _Elements)
                    _ValidateElement((AElement)elem,ref exceptions);
            }
            if (exceptions.Count != 0)
            {
                Exception ex = new InvalidProcessDefinitionException(exceptions);
                WriteLogException(new StackFrame(1, true), DateTime.Now, ex);
                throw ex;
            }
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Time to load Process Document {0}ms",DateTime.Now.Subtract(start).TotalMilliseconds));
            _state = new ProcessState(new ProcessStepComplete(_ProcessStepComplete), new ProcessStepError(_ProcessStepError));
        }

        private void _ValidateElement(AElement elem,ref List<Exception> exceptions)
        {
            WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Validating element {0}", new object[] { elem.id }));
            foreach (RequiredAttribute ra in Utility.GetCustomAttributesForClass(elem.GetType(),typeof(RequiredAttribute)))
            {
               if (elem[ra.Name]==null)
                    exceptions.Add(new MissingAttributeException(elem.Element,ra));
            }
            foreach (AttributeRegex ar in Utility.GetCustomAttributesForClass(elem.GetType(), typeof(AttributeRegex)))
            {
                if (!ar.IsValid(elem))
                    exceptions.Add(new InvalidAttributeValueException(elem.Element, ar));
            }
            string[] err;
            if (!elem.IsValid(out err))
                exceptions.Add(new InvalidElementException(elem.Element, err));
            if (elem.ExtensionElement != null)
                _ValidateElement((ExtensionElements)elem.ExtensionElement, ref exceptions);
            if (elem is AParentElement)
            {
                foreach (AElement e in ((AParentElement)elem).Children)
                    _ValidateElement(e,ref exceptions);
            }
        }

        public bool LoadState(XmlDocument doc)
        {
            return LoadState(doc, false);
        }

        public bool LoadState(XmlDocument doc,bool autoResume)
        {
            _current = this;
            WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, "Loading state for Business Process");
            if (_state.Load(doc))
            {
                WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "State loaded for Business Process");
                _isSuspended = _state.IsSuspended;
                if (autoResume&&_isSuspended)
                    Resume();
                return true;
            }
            return false;
        }

        public void Resume()
        {
            _current = this;
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "Attempting to resmue Business Process");
            if (_isSuspended)
            {
                _isSuspended = false;
                sSuspendedStep[] resumeSteps = _state.ResumeSteps;
                _state.Resume();
                if (resumeSteps != null)
                {
                    foreach (sSuspendedStep ss in resumeSteps)
                        _ProcessStepComplete(ss.IncomingID, ss.ElementID);
                }
                foreach (sStepSuspension ss in _state.SuspendedSteps)
                {
                    Thread th = new Thread(new ParameterizedThreadStart(_suspendEvent));
                    th.Start((object)(new object[] { ss.id, ss.EndTime }));
                }
                WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "Business Process Resume Complete");
            }
            else
            {
                Exception ex = new NotSuspendedException();
                WriteLogException(new StackFrame(1, true), DateTime.Now, ex);
                throw ex;
            }
        }

        private void _suspendEvent(object parameters)
        {
            _current = this;
            string id = (string)((object[])parameters)[0];
            DateTime release = (DateTime)((object[])parameters)[1];
            TimeSpan ts = release.Subtract(DateTime.Now);
            if (ts.TotalMilliseconds > 0)
                Utility.Sleep(ts);
            IElement elem = _GetElement(id);
            if (elem != null)
            {
                AEvent evnt = (AEvent)elem;
                lock (_state) { _state.Path.SucceedEvent(evnt); }
                if (_onEventCompleted != null)
                    _onEventCompleted(evnt, new ReadOnlyProcessVariablesContainer(evnt.id, _state, this));
            }
        }

        public Bitmap Diagram(bool outputVariables)
        {
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Rendering Business Process Diagram{0}",new object[] { (outputVariables ? " with variables" : " without variables") }));
            int width = 0;
            int height = 0;
            foreach (IElement elem in _Elements)
            {
                if (elem is Definition)
                {
                    foreach (Diagram d in ((Definition)elem).Diagrams)
                    {
                        Size s = d.Size;
                        width = Math.Max(width, s.Width + _DEFAULT_PADDING);
                        height += _DEFAULT_PADDING + s.Height;
                    }
                }
            }
            Bitmap ret = new Bitmap(width, height);
            Graphics gp = Graphics.FromImage(ret);
            gp.FillRectangle(Brushes.White, new Rectangle(0, 0, width, height));
            int padding = _DEFAULT_PADDING / 2;
            foreach (IElement elem in _Elements)
            {
                if (elem is Definition)
                {
                    foreach (Diagram d in ((Definition)elem).Diagrams)
                    {
                        gp.DrawImage(d.Render(_state.Path, ((Definition)elem)), new Point(_DEFAULT_PADDING / 2, padding));
                        padding += d.Size.Height + _DEFAULT_PADDING;
                    }
                }
            }
            if (outputVariables)
                ret = _AppendVariables(ret, gp);
            return ret;
        }

        private Bitmap _AppendVariables(Bitmap ret, Graphics gp)
        {
            SizeF sz = gp.MeasureString("Variables", Constants.FONT);
            int varHeight = (int)sz.Height + 2;
            string[] keys = _state[null];
            foreach (string str in keys)
                varHeight += (int)gp.MeasureString(str, Constants.FONT).Height + 2;
            Bitmap vmap = new Bitmap(_VARIABLE_IMAGE_WIDTH, varHeight);
            gp = Graphics.FromImage(vmap);
            gp.FillRectangle(Brushes.White, new Rectangle(0, 0, vmap.Width, vmap.Height));
            Pen p = new Pen(Brushes.Black, Constants.PEN_WIDTH);
            gp.DrawRectangle(p, new Rectangle(0, 0, vmap.Width, vmap.Height));
            gp.DrawLine(p, new Point(0, (int)sz.Height + 2), new Point(_VARIABLE_IMAGE_WIDTH, (int)sz.Height + 2));
            gp.DrawLine(p, new Point(_VARIABLE_NAME_WIDTH, (int)sz.Height + 2), new Point(_VARIABLE_NAME_WIDTH, vmap.Height));
            gp.DrawString("Variables", Constants.FONT, Brushes.Black, new PointF((vmap.Width - sz.Width) / 2, 2));
            int curY = (int)sz.Height + 2;
            for (int x = 0; x < keys.Length; x++)
            {
                string label = keys[x];
                SizeF szLabel = gp.MeasureString(keys[x], Constants.FONT);
                while (szLabel.Width > _VARIABLE_NAME_WIDTH)
                {
                    if (label.EndsWith("..."))
                        label = label.Substring(0, label.Length - 4) + "...";
                    else
                        label = label.Substring(0, label.Length - 1) + "...";
                    szLabel = gp.MeasureString(label, Constants.FONT);
                }
                string val = "";
                if (_state[null, keys[x]] != null)
                {
                    if (_state[null, keys[x]].GetType().IsArray)
                    {
                        val = "";
                        foreach (object o in (IEnumerable)_state[null, keys[x]])
                            val += string.Format("{0},", o);
                        val = val.Substring(0, val.Length - 1);
                    }else if (_state[null,keys[x]] is Hashtable)
                    {
                        val = "{";
                        foreach (string key in ((Hashtable)_state[null, keys[x]]).Keys)
                            val += string.Format("{{\"{0}\":\"{1}\"}},", key, ((Hashtable)_state[null, keys[x]])[key]);
                        val = val.Substring(0, val.Length - 1)+"}";
                    }
                    else
                        val = _state[null, keys[x]].ToString();
                }
                SizeF szValue = gp.MeasureString(val, Constants.FONT);
                if (szValue.Width > _VARIABLE_VALUE_WIDTH)
                {
                    if (val.EndsWith("..."))
                        val = val.Substring(0, val.Length - 4) + "...";
                    else
                        val = val.Substring(0, val.Length - 1) + "...";
                    szValue = gp.MeasureString(val, Constants.FONT);
                }
                gp.DrawString(label, Constants.FONT, Brushes.Black, new Point(2, curY));
                gp.DrawString(val, Constants.FONT, Brushes.Black, new Point(2 + _VARIABLE_NAME_WIDTH, curY));
                curY += (int)Math.Max(szLabel.Height, szValue.Height) + 2;
                gp.DrawLine(p, new Point(0, curY), new Point(_VARIABLE_IMAGE_WIDTH, curY));
            }
            gp.Flush();
            Bitmap tret = new Bitmap(ret.Width + _DEFAULT_PADDING + vmap.Width, Math.Max(ret.Height, vmap.Height + _DEFAULT_PADDING));
            gp = Graphics.FromImage(tret);
            gp.FillRectangle(Brushes.White, new Rectangle(0, 0, tret.Width, tret.Height));
            gp.DrawImage(ret, new Point(0, 0));
            gp.DrawImage(vmap, new Point(ret.Width + _DEFAULT_PADDING, _DEFAULT_PADDING));
            gp.Flush();
            return tret;
        }

        public byte[] Animate(bool outputVariables)
        {
            _current = this;
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Rendering Business Process Animation{0}", new object[] { (outputVariables ? " with variables" : " without variables") }));
            MemoryStream ms = new MemoryStream();
            using (Drawing.GifEncoder enc = new Drawing.GifEncoder(ms))
            {
                enc.FrameDelay = _ANIMATION_DELAY;
                _state.Path.StartAnimation();
                Bitmap bd = Diagram(false);
                Graphics gp = Graphics.FromImage(bd);
                enc.AddFrame((outputVariables ? _AppendVariables(bd, gp) : bd));
                while (_state.Path.HasNext())
                {
                    string nxtStep = _state.Path.MoveToNextStep();
                    if (nxtStep != null)
                    {
                        int padding = _DEFAULT_PADDING / 2;
                        foreach (IElement elem in _Elements)
                        {
                            if (elem is Definition)
                            {
                                foreach (Diagram d in ((Definition)elem).Diagrams)
                                {
                                    if (d.RendersElement(nxtStep))
                                    {
                                        gp.DrawImage(d.UpdateState(_state.Path, ((Definition)elem), nxtStep), new Point(_DEFAULT_PADDING / 2, padding));
                                        break;
                                    }
                                    padding += d.Size.Height + _DEFAULT_PADDING;
                                }
                            }
                        }
                        enc.AddFrame((outputVariables ? _AppendVariables(bd, gp) : bd));
                    }
                }
                _state.Path.FinishAnimation();
            }
            return ms.ToArray();
        }

        public BusinessProcess Clone(bool includeState,bool includeDelegates)
        {
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Cloning Business Process {0} {1}",new object[] {
                (includeState ? "including state":"without state"),
                (includeDelegates ? "including delegates" : "without delegates")
            }));
            BusinessProcess ret = new BusinessProcess();
            ret._doc = _doc;
            ret._components = new List<object>(_components.ToArray());
            ret._constants = _constants;
            ret._elements = _elements;
            if (includeState)
                ret._state = _state;
            else
                ret._state = new ProcessState(new ProcessStepComplete(ret._ProcessStepComplete), new ProcessStepError(ret._ProcessStepError));
            if (includeDelegates)
            {
                ret.OnEventStarted = OnEventStarted;
                ret.OnEventCompleted = OnEventCompleted;
                ret.OnEventError = OnEventError;
                ret.OnTaskStarted = OnTaskStarted;
                ret.OnTaskCompleted = OnTaskCompleted;
                ret.OnTaskError = OnTaskError;
                ret.OnProcessStarted = OnProcessStarted;
                ret.OnProcessCompleted = OnProcessCompleted;
                ret.OnProcessError = OnProcessError;
                ret.OnSubProcessStarted = OnSubProcessStarted;
                ret.OnSubProcessCompleted = OnSubProcessCompleted;
                ret.OnSubProcessError = OnSubProcessError;
                ret.OnSequenceFlowCompleted = OnSequenceFlowCompleted;
                ret.OnMessageFlowCompleted = OnMessageFlowCompleted;
                ret.IsEventStartValid = IsEventStartValid;
                ret.IsProcessStartValid = IsProcessStartValid;
                ret.IsFlowValid = IsFlowValid;
                ret.ProcessBusinessRuleTask = ProcessBusinessRuleTask;
                ret.BeginManualTask = BeginManualTask;
                ret.ProcessRecieveTask = ProcessRecieveTask;
                ret.ProcessScriptTask = ProcessScriptTask;
                ret.ProcessSendTask = ProcessSendTask;
                ret.ProcessServiceTask = ProcessServiceTask;
                ret.ProcessTask = ProcessTask;
                ret.BeginUserTask = BeginUserTask;
                ret.LogException = LogException;
                ret.LogLine = LogLine;
            }
            return ret;
        }

        public bool BeginProcess(ProcessVariablesContainer variables)
        {
            _current = this;
            variables.SetProcess(this);
            WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, "Attempting to begin process");
            bool ret = false;
            foreach (IElement elem in _elements.Values)
            {
                if (elem is Elements.Process)
                {
                    if (((Elements.Process)elem).IsStartValid(variables, _isProcessStartValid))
                    {
                        Elements.Process p = (Elements.Process)elem;
                        foreach (StartEvent se in p.StartEvents)
                        {
                            if (se.IsEventStartValid(variables, _isEventStartValid))
                            {
                                WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Valid Process Start[{0}] located, beginning process", se.id));
                                if (_onProcessStarted != null)
                                    _onProcessStarted(p, new ReadOnlyProcessVariablesContainer(variables));
                                if (_onEventStarted!=null)
                                    _onEventStarted(se, new ReadOnlyProcessVariablesContainer(variables));
                                _state.Path.StartEvent(se, null);
                                foreach (string str in variables.Keys)
                                    _state[se.id,str]=variables[str];
                                _state.Path.SucceedEvent(se);
                                if (_onEventCompleted!=null)
                                    _onEventCompleted(se, new ReadOnlyProcessVariablesContainer(se.id, _state,this));
                                ret=true;
                            }
                        }
                    }
                }
                if (ret)
                    break;
            }
            if (!ret)
                WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "Unable to begin process, no valid start located");
            return ret;
        }

        public void Suspend()
        {
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "Suspending Business Process");
            _isSuspended = true;
            _state.Suspend();
            _mreSuspend.WaitOne(5000);
            if (_state.OnStateChange != null)
                _state.OnStateChange(_state.Document);
        }

        #region ProcessLock

        public bool WaitForCompletion()
        {
            return _processLock.WaitOne();
        }

        public bool WaitForCompletion(int millisecondsTimeout)
        {
            return _processLock.WaitOne(millisecondsTimeout);
        }

        public bool WaitForCompletion(TimeSpan timeout)
        {
            return _processLock.WaitOne(timeout);
        }

        public bool WaitForCompletion(int millisecondsTimeout,bool exitContext)
        {
            return _processLock.WaitOne(millisecondsTimeout,exitContext);
        }

        public bool WaitForCompletion(TimeSpan timeout,bool exitContext)
        {
            return _processLock.WaitOne(timeout,exitContext);
        }

        #endregion

        private void _ProcessStepComplete(string sourceID,string outgoingID) {
            _current = this;
            WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Process Step[{0}] has been completed", sourceID));
            if (outgoingID != null)
            {
                IElement elem = _GetElement(outgoingID);
                if (elem != null)
                    _ProcessElement(sourceID, elem);
            }
        }

        private void _ProcessStepError(IElement step,Exception ex) {
            _current = this;
            WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, "Process Step Error occured, checking for valid Intermediate Catch Event");
            bool success = false;
            if (step is ATask)
            {
                ATask atsk = (ATask)step;
                string destID = atsk.CatchEventPath(ex);
                if (destID != null)
                {
                    WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Valid Error handle located at {0}", destID));
                    success = true;
                    _ProcessElement(step.id, atsk.Definition.LocateElement(destID));
                }
            }
            Definition def = null;
            if (!success)
            {
                foreach (IElement elem in _Elements)
                {
                    if (elem is Definition)
                    {
                        if (((Definition)elem).LocateElement(step.id) != null)
                        {
                            def = (Definition)elem;
                            break;
                        }
                    }
                }
                if (def != null)
                {
                    IElement[] catchers = def.LocateElementsOfType(typeof(IntermediateCatchEvent));
                    foreach (IntermediateCatchEvent catcher in catchers)
                    {
                        string[] tmp = catcher.ErrorTypes;
                        if (tmp != null)
                        {
                            if (new List<string>(tmp).Contains(ex.Message) || new List<string>(tmp).Contains(ex.GetType().Name))
                            {
                                WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Valid Error handle located at {0}", catcher.id));
                                success = true;
                                _ProcessElement(step.id, catcher);
                                break;
                            }
                        }
                    }
                    if (!success)
                    {
                        foreach (IntermediateCatchEvent catcher in catchers)
                        {
                            string[] tmp = catcher.ErrorTypes;
                            if (tmp != null)
                            {
                                if (new List<string>(tmp).Contains("*"))
                                {
                                    WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Valid Error handle located at {0}", catcher.id));
                                    success = true;
                                    _ProcessElement(step.id, catcher);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            if (!success)
            {
                if (_onProcessError!=null)
                    _onProcessError.Invoke(((IStepElement)step).Process,step,new ReadOnlyProcessVariablesContainer(step.id,_state,this,ex));
            }
        }

        private void _ProcessElement(string sourceID,IElement elem)
        {
            if (_isSuspended)
            {
                _state.Path.SuspendElement(sourceID, elem);
                _mreSuspend.Set();
            }
            else
            {
                WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Processing Element {0} from source {1}", new object[] { elem.id, sourceID }));
                _current = this;
                if (elem is SequenceFlow)
                {
                    SequenceFlow sf = (SequenceFlow)elem;
                    lock (_state)
                    {
                        _state.Path.ProcessSequenceFlow(sf);
                    }
                    if (_onSequenceFlowCompleted != null)
                        _onSequenceFlowCompleted(sf,new ReadOnlyProcessVariablesContainer(elem.id,_state,this));
                }
                else if (elem is MessageFlow)
                {
                    MessageFlow mf = (MessageFlow)elem;
                    lock (_state)
                    {
                        _state.Path.ProcessMessageFlow(mf);
                    }
                    if (_onMessageFlowCompleted != null)
                        _onMessageFlowCompleted(mf, new ReadOnlyProcessVariablesContainer(elem.id, _state,this));
                }
                else if (elem is AGateway)
                {
                    AGateway gw = (AGateway)elem;
                    Definition def = null;
                    foreach (IElement e in _Elements)
                    {
                        if (e is Definition)
                        {
                            if (((Definition)e).LocateElement(gw.id) != null)
                            {
                                def = (Definition)e;
                                break;
                            }
                        }
                    }
                    lock (_state)
                    {
                        _state.Path.StartGateway(gw, sourceID);
                    }
                    if (_onGatewayStarted != null)
                        _onGatewayStarted(gw, new ReadOnlyProcessVariablesContainer(elem.id, _state,this));
                    string[] outgoings = null;
                    try
                    {
                        outgoings = gw.EvaulateOutgoingPaths(def, _isFlowValid, new ProcessVariablesContainer(elem.id, _state,this));
                    }
                    catch (Exception e)
                    {
                        WriteLogException(new StackFrame(1, true), DateTime.Now, e);
                        if (_onGatewayError != null)
                            _onGatewayError(gw, new ReadOnlyProcessVariablesContainer(elem.id, _state,this));
                        outgoings = null;
                    }
                    lock (_state)
                    {
                        if (outgoings == null)
                            _state.Path.FailGateway(gw);
                        else
                            _state.Path.SuccessGateway(gw, outgoings);
                    }
                }
                else if (elem is AEvent)
                {
                    AEvent evnt = (AEvent)elem;
                    if (evnt is IntermediateCatchEvent)
                    {
                        SubProcess sp = evnt.SubProcess;
                        if (sp != null)
                            _state.Path.StartSubProcess(sp, sourceID);
                    }
                    lock (_state)
                    {
                        _state.Path.StartEvent(evnt, sourceID);
                    }
                    if (_onEventStarted != null)
                        _onEventStarted(evnt, new ReadOnlyProcessVariablesContainer(elem.id, _state, this));
                    bool success = true;
                    if (evnt is IntermediateCatchEvent || evnt is IntermediateThrowEvent)
                    {
                        TimeSpan? ts = evnt.GetTimeout(new ProcessVariablesContainer(evnt.id, _state,this));
                        if (ts.HasValue)
                        {
                            lock (_state)
                            {
                                _state.SuspendStep(evnt.id, ts.Value);
                            }
                            if (ts.Value.TotalMilliseconds > 0)
                                Utility.Sleep(ts.Value);
                            success = true;
                        }
                    }else if (_isEventStartValid != null && (evnt is IntermediateCatchEvent || evnt is StartEvent))
                    {
                        try
                        {
                            success = _isEventStartValid(evnt, new ProcessVariablesContainer(evnt.id, _state, this));
                        }
                        catch (Exception e)
                        {
                            WriteLogException(new StackFrame(1, true), DateTime.Now, e);
                            success = false;
                        }
                    }
                    if (!success)
                    {
                        lock (_state) { _state.Path.FailEvent(evnt); }
                        if (_onEventError != null)
                            _onEventError(evnt, new ReadOnlyProcessVariablesContainer(elem.id, _state,this));
                    }
                    else
                    {
                        lock (_state) { _state.Path.SucceedEvent(evnt); }
                        if (_onEventCompleted != null)
                            _onEventCompleted(evnt, new ReadOnlyProcessVariablesContainer(elem.id, _state,this));
                        if (evnt is EndEvent)
                        {
                            if (((EndEvent)evnt).IsProcessEnd)
                            {
                                SubProcess sp = ((EndEvent)evnt).SubProcess;
                                if (sp != null)
                                {
                                    lock (_state) { _state.Path.SucceedSubProcess(sp); }
                                    if (_onSubProcessCompleted != null)
                                        _onSubProcessCompleted(sp, new ReadOnlyProcessVariablesContainer(sp.id, _state, this));            
                                }
                                else
                                {
                                    if (_onProcessCompleted != null)
                                        _onProcessCompleted(((EndEvent)evnt).Process, new ReadOnlyProcessVariablesContainer(elem.id, _state, this));
                                    _processLock.Set();
                                }
                            }
                        }
                    }
                }
                else if (elem is ATask)
                {
                    ATask tsk = (ATask)elem;
                    lock (_state)
                    {
                        _state.Path.StartTask(tsk, sourceID);
                    }
                    if (_onTaskStarted != null)
                        _onTaskStarted(tsk, new ReadOnlyProcessVariablesContainer(elem.id, _state, this));
                    try
                    {
                        ProcessVariablesContainer variables = new ProcessVariablesContainer(tsk.id, _state,this);
                        switch (elem.GetType().Name)
                        {
                            case "BusinessRuleTask":
                                _processBusinessRuleTask(tsk, ref variables);
                                _MergeVariables(tsk, variables);
                                break;
                            case "ManualTask":
                                _beginManualTask(tsk, variables, new CompleteManualTask(_CompleteExternalTask), new ErrorManualTask(_ErrorExternalTask));
                                break;
                            case "RecieveTask":
                                _processRecieveTask(tsk, ref variables);
                                _MergeVariables(tsk, variables);
                                break;
                            case "ScriptTask":
                                ((ScriptTask)tsk).ProcessTask(ref variables, _processScriptTask);
                                _MergeVariables(tsk, variables);
                                break;
                            case "SendTask":
                                _processSendTask(tsk, ref variables);
                                _MergeVariables(tsk, variables);
                                break;
                            case "ServiceTask":
                                _processServiceTask(tsk, ref variables);
                                _MergeVariables(tsk, variables);
                                break;
                            case "Task":
                                _processTask(tsk, ref variables);
                                _MergeVariables(tsk, variables);
                                break;
                            case "UserTask":
                               _beginUserTask(tsk, variables, new CompleteUserTask(_CompleteUserTask), new ErrorUserTask(_ErrorExternalTask));
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        WriteLogException(new StackFrame(1, true), DateTime.Now, e);
                        if (_onTaskError != null)
                            _onTaskError(tsk, new ReadOnlyProcessVariablesContainer(elem.id, _state,this,e));
                        lock (_state) { _state.Path.FailTask(tsk,e); }
                    }
                }else if (elem is SubProcess)
                {
                    SubProcess esp = (SubProcess)elem;
                    ProcessVariablesContainer variables = new ProcessVariablesContainer(elem.id, _state, this);
                    if (esp.IsStartValid(variables, _isProcessStartValid))
                    {
                        foreach (StartEvent se in esp.StartEvents)
                        {
                            if (se.IsEventStartValid(variables, _isEventStartValid))
                            {
                                WriteLogLine(LogLevels.Info, new StackFrame(1, true), DateTime.Now, string.Format("Valid Sub Process Start[{0}] located, beginning process", se.id));
                                lock (_state) { _state.Path.StartSubProcess(esp, sourceID); }
                                if (_onSubProcessStarted!= null)
                                    _onSubProcessStarted(esp, new ReadOnlyProcessVariablesContainer(variables));
                                if (_onEventStarted != null)
                                    _onEventStarted(se, new ReadOnlyProcessVariablesContainer(variables));
                                _state.Path.StartEvent(se, null);
                                _state.Path.SucceedEvent(se);
                                if (_onEventCompleted != null)
                                    _onEventCompleted(se, new ReadOnlyProcessVariablesContainer(se.id, _state, this));
                            }
                        }
                    }
                }
            }
        }

        private void _MergeVariables(UserTask task, ProcessVariablesContainer variables, string completedByID)
        {
            _MergeVariables((ATask)task, variables, completedByID);
        }

        private void _MergeVariables(ATask task, ProcessVariablesContainer variables)
        {
            _MergeVariables(task, variables, null);
        }

        private void _MergeVariables(ATask task, ProcessVariablesContainer variables,string completedByID)
        {
            WriteLogLine(LogLevels.Debug, new StackFrame(1, true), DateTime.Now, string.Format("Merging variables from Task[{0}] complete by {1} into the state", new object[] { task.id, completedByID }));
            lock (_state)
            {
                foreach (string str in variables.Keys)
                {
                    object left = variables[str];
                    object right = _state[task.id, str];
                    if (!_IsVariablesEqual(left,right))
                        _state[task.id, str] = left;
                }
                if (_onTaskCompleted != null)
                    _onTaskCompleted(task, new ReadOnlyProcessVariablesContainer(task.id, _state,this));
                if (task is UserTask)
                    _state.Path.SucceedTask((UserTask)task,completedByID);
                else
                    _state.Path.SucceedTask(task);
            }
        }

        private bool _IsVariablesEqual(object left, object right)
        {
            if (left == null && right != null)
                return false;
            else if (left != null && right == null)
                return false;
            else if (left == null && right == null)
                return true;
            else 
            {
                if (left is Array)
                {
                    if (!(right is Array))
                        return false;
                    else
                    {
                        Array aleft = (Array)left;
                        Array aright = (Array)right;
                        if (aleft.Length != aright.Length)
                            return false;
                        for (int x = 0; x < aleft.Length; x++)
                        {
                            if (!_IsVariablesEqual(aleft.GetValue(x), aright.GetValue(x)))
                                return false;
                        }
                        return true;
                    }
                }
                else if (left is Hashtable)
                {
                    if (!(right is Hashtable))
                        return false;
                    else
                    {
                        Hashtable hleft = (Hashtable)left;
                        Hashtable hright = (Hashtable)right;
                        if (hleft.Count != hright.Count)
                            return false;
                        foreach (object key in hleft.Keys)
                        {
                            if (!hright.ContainsKey(key))
                                return false;
                            else if (!_IsVariablesEqual(hleft[key], hright[key]))
                                return false;
                        }
                        foreach (object key in hright.Keys)
                        {
                            if (!hleft.ContainsKey(key))
                                return false;
                        }
                        return true;
                    }
                }
                else
                {
                    try { return left.Equals(right); }
                    catch (Exception e) { return false; }
                }
            }
        }

        #region Logging
        internal void WriteLogLine(LogLevels level,StackFrame sf,DateTime timestamp, string message)
        {
            if ((int)level <= (int)_stateLogLevel && _state!=null)
                _state.LogLine(sf.GetMethod().DeclaringType.Assembly.GetName(), sf.GetFileName(), sf.GetFileLineNumber(), level, timestamp, message);
            if (_logLine != null)
                _logLine.Invoke(sf.GetMethod().DeclaringType.Assembly.GetName(), sf.GetFileName(), sf.GetFileLineNumber(), level, timestamp, message);
        }

        internal void WriteLogException(StackFrame sf, DateTime timestamp, Exception exception)
        {
            if ((int)LogLevels.Error <= (int)_stateLogLevel)
                _state.LogException(sf.GetMethod().DeclaringType.Assembly.GetName(), sf.GetFileName(), sf.GetFileLineNumber(), timestamp, exception);
            if (_logException != null)
                _logException.Invoke(sf.GetMethod().DeclaringType.Assembly.GetName(), sf.GetFileName(), sf.GetFileLineNumber(), timestamp, exception);
        }
        #endregion
    }
}
