using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.Rd.Base;
using JetBrains.Rd.Impl;
using JetBrains.Serialization;

namespace JetBrains.Rd
{
  public struct SerializationCtx
  {
    public ISerializers Serializers { get; private set; }

    [NotNull] public readonly IDictionary<string, IInternRoot<object>> InternRoots;

    public SerializationCtx(ISerializers serializers, IDictionary<string, IInternRoot<object>> internRoots = null) : this()
    {
      Serializers = serializers;
      InternRoots = internRoots ?? new Dictionary<string, IInternRoot<object>>();
    }

    public SerializationCtx(IProtocol protocol, IDictionary<string, IInternRoot<object>> internRoots = null) : this(protocol.Serializers, internRoots)
    {
    }

    public SerializationCtx WithInternRootsHere(RdBindableBase owner, params string[] keys)
    {
      var newInternRoots = new Dictionary<string, IInternRoot<object>>(InternRoots);
      foreach (var key in keys)
      {
        var root = owner.GetOrCreateHighPriorityExtension("InternRoot-" + key, () => new InternRoot<object> { RdId = owner.RdId.Mix(".InternRoot-" + key) });
        newInternRoots[key] = root;
      }
      return new SerializationCtx(Serializers, newInternRoots);
    }
    
    public T ReadInterned<T>(UnsafeReader stream, string internKey, CtxReadDelegate<T> readValueDelegate)
    {
      if (!InternRoots.TryGetValue(internKey, out var interningRoot))
        return readValueDelegate(this, stream);

      var id = InternId.Read(stream);
      if (id.IsValid)
        return interningRoot.UnIntern<T>(id);
      return readValueDelegate(this, stream);
    }

    public void WriteInterned<T>(UnsafeWriter stream, T value, string internKey, CtxWriteDelegate<T> writeValueDelegate)
    {
      if (!InternRoots.TryGetValue(internKey, out var interningRoot))
      {
        writeValueDelegate(this, stream, value);
        return;
      }

      var id = interningRoot.Intern(value);
      InternId.Write(stream, id);
      if (!id.IsValid)
        writeValueDelegate(this, stream, value);
    }
  }
}