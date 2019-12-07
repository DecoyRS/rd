﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Collections.Viewable;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;
using JetBrains.Rd.Impl;
using JetBrains.Serialization;

namespace JetBrains.Rd.Base
{
  public abstract class RdExtBase : RdReactiveBase
  {
    public enum ExtState
    {
      Ready,
      ReceivedCounterPart,
      Disconnected
    }

    
    
    private readonly ExtWire myExtWire = new ExtWire();
    private IProtocol myExtProtocol;
    public override IProtocol Proto => myExtProtocol ?? base.Proto;
    
    
    public readonly IReadonlyProperty<bool> Connected;
    protected RdExtBase()
    {
      Connected = myExtWire.Connected;
    }

    protected abstract Action<ISerializers> Register { get; }
    protected virtual long SerializationHash => 0L;

    public override IScheduler WireScheduler { get { return SynchronousScheduler.Instance; } }

    protected override void Init(Lifetime lifetime)
    {
      TraceMe(Protocol.InitializationLogger, "binding");

      var parentProtocol = base.Proto;
      var parentWire = parentProtocol.Wire;
      
      parentProtocol.Serializers.RegisterToplevelOnce(GetType(), Register);
      

      //todo ExtScheduler
      myExtWire.RealWire = parentWire;
      lifetime.Bracket(
        () => { myExtProtocol = new Protocol(parentProtocol.Name, parentProtocol.Serializers, parentProtocol.Identities, parentProtocol.Scheduler, myExtWire, lifetime, SerializationContext, parentProtocol.Contexts); },
        () => { myExtProtocol = null; }
        );
        
      parentWire.Advise(lifetime, this);
            
      
      SendState(parentWire, ExtState.Ready);
      lifetime.OnTermination(() => { SendState(parentWire, ExtState.Disconnected); });
      
      
      //protocol must be set first to allow bindable bind to it
      base.Init(lifetime);
      
      TraceMe<Func<string>>(Protocol.InitializationLogger, "created and bound :: {0}", this.PrintToString);
    }


    public override void OnWireReceived(UnsafeReader reader)
    {
      var remoteState = (ExtState)reader.ReadInt();
      TraceMe(LogReceived, "remote: {0}", remoteState);

      switch (remoteState)
      {
        case ExtState.Ready:
          SendState(myExtWire.RealWire, ExtState.ReceivedCounterPart);
          myExtWire.Connected.Set(true);
          break;
          
        case ExtState.ReceivedCounterPart:
          myExtWire.Connected.Set(true); //don't set anything if already set
          break;
          
        case ExtState.Disconnected:
          myExtWire.Connected.Set(false);
          break;
          
        default:
          throw new ArgumentOutOfRangeException("Unsupported state: "+remoteState);
      }
      
      var counterpartSerializationHash = reader.ReadLong();
      if (counterpartSerializationHash != SerializationHash)
      {                                    
        base.Proto.Scheduler.Queue(() => { base.Proto.OutOfSyncModels.Add(this); } );
        
        if (base.Proto is Protocol p && p.ThrowErrorOnOutOfSyncModels)
          Assertion.Fail($"serializationHash of ext '{Location}' doesn't match to counterpart: maybe you forgot to generate models?");
      }
    }

    private void SendState(IWire parentWire, ExtState state)
    {
      parentWire.Send(RdId, writer =>
      {
        TraceMe(LogSend, state);
        writer.Write((int)state);
        writer.Write(SerializationHash);
      });
    }
    
    
    [StringFormatMethod("fmt")]
    private void TraceMe<T>(ILog logger, string fmt, T paramProvider)
    {
      if (!logger.IsTraceEnabled()) return;

      string param;
      var fparam = paramProvider as Func<object>;  
      if (fparam != null)
      {
        param = fparam()?.ToString();
      }
      else
      {
        param = paramProvider?.ToString();
      }
      
      logger.Trace("ext `{0}` ({1}) :: {2}", Location, RdId, string.Format(fmt, param));
    }
    
    
    private void TraceMe<T>(ILog logger, T msgProvider)
    {
      TraceMe(logger, "{0}", msgProvider);
    }
    
        

    protected override void InitBindableFields(Lifetime lifetime)
    {
      foreach (var pair in BindableChildren)
      {
        if (pair.Value is RdPropertyBase reactive)
        {
          using (reactive.UsingLocalChange())
          {
            reactive.BindPolymorphic(lifetime, this, pair.Key);
          } 
        }
        else
        {
          pair.Value?.BindPolymorphic(lifetime, this, pair.Key);
        }
      }
      
    }
  }

  
  
  class ExtWire : IWire
  {


    internal readonly ViewableProperty<bool> Connected = new ViewableProperty<bool>(false);
    internal IWire RealWire;
    
    private struct QueueItem
    {
      public readonly RdId Id;
      public readonly byte[] Bytes;
      public readonly KeyValuePair<RdContextBase, object>[] StoredContext;

      public QueueItem(RdId id, byte[] bytes, KeyValuePair<RdContextBase, object>[] storedContext)
      {
        Id = id;
        Bytes = bytes;
        StoredContext = storedContext;
      }
    }
    
    
    private readonly Queue<QueueItem> mySendQ = new Queue<QueueItem>();
    
    public ProtocolContexts Contexts
    {
      get => RealWire.Contexts;
      set => Assertion.Assert(RealWire.Contexts == value, "Can't change ProtocolContexts in ExtWire");
    }


    public ExtWire()
    {
      Connected.WhenTrue(Lifetime.Eternal, _ =>
      {
        lock (mySendQ)
        {
          while (mySendQ.Count > 0)
          {
            var p = mySendQ.Dequeue();
            foreach (var keyValuePair in p.StoredContext)
              keyValuePair.Key.PushContextBoxed(keyValuePair.Value);

            try
            {
              RealWire.Send(p.Id, writer => writer.WriteRaw(p.Bytes, 0, p.Bytes.Length));
            }
            finally
            {
              foreach (var keyValuePair in p.StoredContext)
                keyValuePair.Key.PopContext();
            }
          }
        }               
      });
    }

    
    public void Send<TContext>(RdId id, TContext param, Action<TContext, UnsafeWriter> writer)
    {
      lock (mySendQ)
      {
        if (mySendQ.Count > 0 || !Connected.Value)
        {
          using (var cookie = UnsafeWriter.NewThreadLocalWriter())
          {
            writer(param, cookie.Writer);
            var storedContext = Contexts.RegisteredContexts
              .Select(it => new KeyValuePair<RdContextBase, object>(it, it.ValueBoxed)).ToArray();
            mySendQ.Enqueue(new QueueItem(id, cookie.CloneData(), storedContext));
          }

          return;
        }
      }

      RealWire.Send(id, param, writer);
    }

    public void Advise(Lifetime lifetime, IRdWireable entity)
    {
      RealWire.Advise(lifetime, entity);
    }
  }
}