namespace WhisperFS

open System

/// Thread-safe ring buffer for bounded audio buffering with overflow handling
type RingBuffer<'T> =
    new: capacity:int -> RingBuffer<'T>

    /// Buffer capacity
    member Capacity: int

    /// Current item count
    member Count: int

    /// Whether buffer is at capacity
    member IsFull: bool

    /// Whether buffer is empty
    member IsEmpty: bool

    /// Write items, overwriting oldest if full
    member Write: items:'T[] -> unit

    /// Read up to maxItems without removing
    member Read: maxItems:int -> 'T[]

    /// Remove items from front of buffer
    member Consume: itemsToConsume:int -> unit

    /// Get all items as array
    member ToArray: unit -> 'T[]

    /// Clear all items
    member Clear: unit -> unit

/// Audio processing utilities for format conversion and analysis
module AudioUtils =

    /// Convert WAV bytes to normalized float32 samples
    val convertWavToFloat32: wavBytes:byte[] -> float32[]

    /// Calculate audio energy for voice activity detection
    val calculateEnergy: samples:float32[] -> float32

    /// Apply low-pass filter for noise reduction
    val lowPassFilter: samples:float32[] -> cutoffFreq:float32 -> sampleRate:float32 -> float32[]

/// Thread-safe event aggregator for pub/sub patterns
type EventAggregator<'T> =
    new: unit -> EventAggregator<'T>

    /// Observable event stream
    member Publish: IObservable<'T>

    /// Trigger event to all subscribers
    member Trigger: value:'T -> unit

    /// Subscribe to events
    member Subscribe: handler:('T -> unit) -> System.IDisposable

    /// Clear all subscriptions
    member Clear: unit -> unit

/// Performance monitoring for transcription operations
module PerformanceMonitor =

    /// Start performance monitoring
    val start: unit -> unit

    /// Stop performance monitoring
    val stop: unit -> unit

    /// Reset all performance counters
    val reset: unit -> unit

    /// Record processed audio duration
    val addAudioProcessed: duration:System.TimeSpan -> unit

    /// Record processed segment
    val addSegment: unit -> unit

    /// Record generated tokens
    val addTokens: count:int -> unit

    /// Record error occurrence
    val addError: unit -> unit

    /// Get current performance metrics
    val getMetrics: unit -> PerformanceMetrics