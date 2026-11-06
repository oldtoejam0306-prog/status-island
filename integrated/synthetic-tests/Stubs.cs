// Compile-only WinRT stand-ins: tests inject a provider and never execute these APIs.
using System;
using System.Collections.Generic;
namespace Windows.Foundation {
 public enum AsyncStatus { Completed }
 public interface IAsyncOperation<T> { Action<IAsyncOperation<T>,AsyncStatus> Completed {get;set;} T GetResults(); void Cancel(); }
}
namespace Windows.Storage.Streams {
 public interface IInputStream {}
 public interface IRandomAccessStreamWithContentType : IDisposable { ulong Size {get;} IInputStream GetInputStreamAt(ulong n); }
 public sealed class DataReader : IDisposable { public DataReader(IInputStream s) {} public Windows.Foundation.IAsyncOperation<uint> LoadAsync(uint n) { throw new NotImplementedException(); } public void ReadBytes(byte[] b) {} public void Dispose() {} }
 public interface IRandomAccessStreamReference { Windows.Foundation.IAsyncOperation<IRandomAccessStreamWithContentType> OpenReadAsync(); }
}
namespace Windows.Media.Control {
 using Windows.Foundation; using Windows.Storage.Streams;
 public enum GlobalSystemMediaTransportControlsSessionPlaybackStatus { Playing }
 public sealed class Controls { public bool IsPlayPauseToggleEnabled,IsPreviousEnabled,IsNextEnabled; }
 public sealed class PlaybackInfo { public Controls Controls; public GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus; }
 public sealed class MediaProperties { public string Title,Artist; public IRandomAccessStreamReference Thumbnail; }
 public sealed class GlobalSystemMediaTransportControlsSession {
  public string SourceAppUserModelId; public IAsyncOperation<MediaProperties> TryGetMediaPropertiesAsync(){throw new NotImplementedException();} public PlaybackInfo GetPlaybackInfo(){throw new NotImplementedException();}
  public IAsyncOperation<bool> TryTogglePlayPauseAsync(){throw new NotImplementedException();} public IAsyncOperation<bool> TrySkipPreviousAsync(){throw new NotImplementedException();} public IAsyncOperation<bool> TrySkipNextAsync(){throw new NotImplementedException();}
 }
 public sealed class GlobalSystemMediaTransportControlsSessionManager { public static IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> RequestAsync(){throw new NotImplementedException();} public IEnumerable<GlobalSystemMediaTransportControlsSession> GetSessions(){throw new NotImplementedException();} }
}
