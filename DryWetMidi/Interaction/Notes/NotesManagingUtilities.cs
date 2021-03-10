﻿using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Melanchall.DryWetMidi.Interaction
{
    /// <summary>
    /// Extension methods for notes managing.
    /// </summary>
    public static class NotesManagingUtilities
    {
        #region Nested classes

        private interface IObjectDescriptor
        {
            bool IsCompleted { get; }

            ITimedObject GetObject();
        }

        private interface IObjectDescriptorIndexed : IObjectDescriptor
        {
            bool IsCompleted { get; }

            Tuple<ITimedObject, int, int> GetIndexedObject();
        }

        private class NoteDescriptor : IObjectDescriptor
        {
            public NoteDescriptor(NoteId noteId, TimedEvent noteOnTimedEvent)
            {
                NoteId = noteId;
                NoteOnTimedEvent = noteOnTimedEvent;
            }

            public NoteId NoteId { get; }

            public TimedEvent NoteOnTimedEvent { get; }

            public TimedEvent NoteOffTimedEvent { get; set; }

            public bool IsCompleted => NoteOffTimedEvent != null;

            public ITimedObject GetObject()
            {
                return IsCompleted ? new Note(NoteOnTimedEvent, NoteOffTimedEvent) : (ITimedObject)NoteOnTimedEvent;
            }
        }

        private sealed class NoteDescriptorIndexed : NoteDescriptor, IObjectDescriptorIndexed
        {
            private readonly int _noteOnIndex;

            public NoteDescriptorIndexed(NoteId noteId, TimedEvent noteOnTimedEvent, int noteOnIndex)
                : base(noteId, noteOnTimedEvent)
            {
                _noteOnIndex = noteOnIndex;
                NoteOffIndex = _noteOnIndex;
            }

            public int NoteOffIndex { get; set; }

            public Tuple<ITimedObject, int, int> GetIndexedObject()
            {
                return Tuple.Create(GetObject(), _noteOnIndex, NoteOffIndex);
            }
        }

        private class TimedEventDescriptor : IObjectDescriptor
        {
            public TimedEventDescriptor(TimedEvent timedEvent)
            {
                TimedEvent = timedEvent;
            }

            public TimedEvent TimedEvent { get; }

            public bool IsCompleted { get; } = true;

            public ITimedObject GetObject()
            {
                return TimedEvent;
            }
        }

        private sealed class TimedEventDescriptorIndexed : TimedEventDescriptor, IObjectDescriptorIndexed
        {
            private readonly int _index;

            public TimedEventDescriptorIndexed(TimedEvent timedEvent, int index)
                : base(timedEvent)
            {
                _index = index;
            }

            public Tuple<ITimedObject, int, int> GetIndexedObject()
            {
                return Tuple.Create(GetObject(), _index, _index);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Sets time and length of the specified note.
        /// </summary>
        /// <param name="note">Note to set time and length to.</param>
        /// <param name="time">Time to set to <paramref name="note"/>.</param>
        /// <param name="length">Length to set to <paramref name="note"/>.</param>
        /// <param name="tempoMap">Tempo map that will be used for time and length conversion.</param>
        /// <returns>An input <paramref name="note"/> with new time and length.</returns>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="note"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="time"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="length"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="tempoMap"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static Note SetTimeAndLength(this Note note, ITimeSpan time, ITimeSpan length, TempoMap tempoMap)
        {
            ThrowIfArgument.IsNull(nameof(note), note);
            ThrowIfArgument.IsNull(nameof(time), time);
            ThrowIfArgument.IsNull(nameof(length), length);
            ThrowIfArgument.IsNull(nameof(tempoMap), tempoMap);

            note.Time = TimeConverter.ConvertFrom(time, tempoMap);
            note.Length = LengthConverter.ConvertFrom(length, note.Time, tempoMap);
            return note;
        }

        /// <summary>
        /// Creates an instance of the <see cref="NotesManager"/> initializing it with the
        /// specified events collection and comparison delegate for events that have same time.
        /// </summary>
        /// <param name="eventsCollection"><see cref="EventsCollection"/> that holds notes to manage.</param>
        /// <param name="sameTimeEventsComparison">Delegate to compare events with the same absolute time.</param>
        /// <returns>An instance of the <see cref="NotesManager"/> that can be used to manage
        /// notes represented by the <paramref name="eventsCollection"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eventsCollection"/> is <c>null</c>.</exception>
        public static NotesManager ManageNotes(this EventsCollection eventsCollection, Comparison<MidiEvent> sameTimeEventsComparison = null)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);

            return new NotesManager(eventsCollection, sameTimeEventsComparison);
        }

        /// <summary>
        /// Creates an instance of the <see cref="NotesManager"/> initializing it with the
        /// events collection of the specified track chunk and comparison delegate for events
        /// that have same time.
        /// </summary>
        /// <param name="trackChunk"><see cref="TrackChunk"/> that holds notes to manage.</param>
        /// <param name="sameTimeEventsComparison">Delegate to compare events with the same absolute time.</param>
        /// <returns>An instance of the <see cref="NotesManager"/> that can be used to manage
        /// notes represented by the <paramref name="trackChunk"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trackChunk"/> is <c>null</c>.</exception>
        public static NotesManager ManageNotes(this TrackChunk trackChunk, Comparison<MidiEvent> sameTimeEventsComparison = null)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);

            return trackChunk.Events.ManageNotes(sameTimeEventsComparison);
        }

        public static IEnumerable<Note> GetNotes(this IEnumerable<MidiEvent> midiEvents)
        {
            ThrowIfArgument.IsNull(nameof(midiEvents), midiEvents);

            var result = new List<Note>();

            foreach (var note in GetNotesAndTimedEventsLazy(midiEvents.GetTimedEventsLazy()).OfType<Note>())
            {
                result.Add(note);
            }

            return result;
        }

        /// <summary>
        /// Gets notes contained in the specified <see cref="EventsCollection"/>.
        /// </summary>
        /// <param name="eventsCollection"><see cref="EventsCollection"/> to search for notes.</param>
        /// <returns>Collection of notes contained in <paramref name="eventsCollection"/> ordered by time.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eventsCollection"/> is <c>null</c>.</exception>
        public static IEnumerable<Note> GetNotes(this EventsCollection eventsCollection)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);

            var result = new List<Note>(eventsCollection.Count / 2);

            foreach (var note in GetNotesAndTimedEventsLazy(eventsCollection.GetTimedEventsLazy()).OfType<Note>())
            {
                result.Add(note);
            }

            return result;
        }

        /// <summary>
        /// Gets notes contained in the specified <see cref="TrackChunk"/>.
        /// </summary>
        /// <param name="trackChunk"><see cref="TrackChunk"/> to search for notes.</param>
        /// <returns>Collection of notes contained in <paramref name="trackChunk"/> ordered by time.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trackChunk"/> is <c>null</c>.</exception>
        public static IEnumerable<Note> GetNotes(this TrackChunk trackChunk)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);

            return trackChunk.Events.GetNotes();
        }

        /// <summary>
        /// Gets notes contained in the specified collection of <see cref="TrackChunk"/>.
        /// </summary>
        /// <param name="trackChunks">Track chunks to search for notes.</param>
        /// <returns>Collection of notes contained in <paramref name="trackChunks"/> ordered by time.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trackChunks"/> is <c>null</c>.</exception>
        public static IEnumerable<Note> GetNotes(this IEnumerable<TrackChunk> trackChunks)
        {
            ThrowIfArgument.IsNull(nameof(trackChunks), trackChunks);

            var eventsCollections = trackChunks.Select(c => c.Events).ToArray();
            var eventsCount = eventsCollections.Sum(e => e.Count);

            var result = new List<Note>(eventsCount / 2);

            foreach (var note in GetNotesAndTimedEventsLazy(eventsCollections.GetTimedEventsLazy(eventsCount).Select(e => e.Item1)).OfType<Note>())
            {
                result.Add(note);
            }

            return result;
        }

        /// <summary>
        /// Gets notes contained in the specified <see cref="MidiFile"/>.
        /// </summary>
        /// <param name="file"><see cref="MidiFile"/> to search for notes.</param>
        /// <returns>Collection of notes contained in <paramref name="file"/> ordered by time.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
        public static IEnumerable<Note> GetNotes(this MidiFile file)
        {
            ThrowIfArgument.IsNull(nameof(file), file);

            return file.GetTrackChunks().GetNotes();
        }

        public static int ProcessNotes(this EventsCollection eventsCollection, Action<Note> action)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);
            ThrowIfArgument.IsNull(nameof(action), action);

            return eventsCollection.ProcessNotes(action, note => true);
        }

        /// <summary>
        /// Performs the specified action on each <see cref="Note"/> contained in the <see cref="EventsCollection"/>.
        /// </summary>
        /// <param name="eventsCollection"><see cref="EventsCollection"/> to search for notes to process.</param>
        /// <param name="action">The action to perform on each <see cref="Note"/> contained in the
        /// <paramref name="eventsCollection"/>.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to process.</param>
        /// <returns>Count of processed notes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="eventsCollection"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="action"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static int ProcessNotes(this EventsCollection eventsCollection, Action<Note> action, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);
            ThrowIfArgument.IsNull(nameof(action), action);
            ThrowIfArgument.IsNull(nameof(match), match);

            return eventsCollection.ProcessNotes(action, match, true);
        }

        public static int ProcessNotes(this TrackChunk trackChunk, Action<Note> action)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);
            ThrowIfArgument.IsNull(nameof(action), action);

            return trackChunk.ProcessNotes(action, note => true);
        }

        /// <summary>
        /// Performs the specified action on each <see cref="Note"/> contained in the <see cref="TrackChunk"/>.
        /// </summary>
        /// <param name="trackChunk"><see cref="TrackChunk"/> to search for notes to process.</param>
        /// <param name="action">The action to perform on each <see cref="Note"/> contained in the
        /// <paramref name="trackChunk"/>.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to process.</param>
        /// <returns>Count of processed notes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="trackChunk"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="action"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static int ProcessNotes(this TrackChunk trackChunk, Action<Note> action, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);
            ThrowIfArgument.IsNull(nameof(action), action);

            return trackChunk.Events.ProcessNotes(action, match);
        }

        public static int ProcessNotes(this IEnumerable<TrackChunk> trackChunks, Action<Note> action)
        {
            ThrowIfArgument.IsNull(nameof(trackChunks), trackChunks);
            ThrowIfArgument.IsNull(nameof(action), action);

            return trackChunks.ProcessNotes(action, note => true);
        }

        /// <summary>
        /// Performs the specified action on each <see cref="Note"/> contained in the collection of
        /// <see cref="TrackChunk"/>.
        /// </summary>
        /// <param name="trackChunks">Collection of <see cref="TrackChunk"/> to search for notes to process.</param>
        /// <param name="action">The action to perform on each <see cref="Note"/> contained in the
        /// <paramref name="trackChunks"/>.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to process.</param>
        /// <returns>Count of processed notes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="trackChunks"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="action"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static int ProcessNotes(this IEnumerable<TrackChunk> trackChunks, Action<Note> action, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(trackChunks), trackChunks);
            ThrowIfArgument.IsNull(nameof(action), action);
            ThrowIfArgument.IsNull(nameof(match), match);

            return trackChunks.ProcessNotes(action, match, true);
        }

        public static int ProcessNotes(this MidiFile file, Action<Note> action)
        {
            ThrowIfArgument.IsNull(nameof(file), file);
            ThrowIfArgument.IsNull(nameof(action), action);

            return file.ProcessNotes(action, note => true);
        }

        /// <summary>
        /// Performs the specified action on each <see cref="Note"/> contained in the <see cref="MidiFile"/>.
        /// </summary>
        /// <param name="file"><see cref="MidiFile"/> to search for notes to process.</param>
        /// <param name="action">The action to perform on each <see cref="Note"/> contained in the
        /// <paramref name="file"/>.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to process.</param>
        /// <returns>Count of processed notes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="file"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="action"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static int ProcessNotes(this MidiFile file, Action<Note> action, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(file), file);
            ThrowIfArgument.IsNull(nameof(action), action);
            ThrowIfArgument.IsNull(nameof(match), match);

            return file.GetTrackChunks().ProcessNotes(action, match);
        }

        public static int RemoveNotes(this EventsCollection eventsCollection)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);

            return eventsCollection.RemoveNotes(note => true);
        }

        /// <summary>
        /// Removes all the <see cref="Note"/> that match the conditions defined by the specified predicate.
        /// </summary>
        /// <param name="eventsCollection"><see cref="EventsCollection"/> to search for notes to remove.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to remove.</param>
        /// <returns>Count of removed notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eventsCollection"/> is <c>null</c>.</exception>
        public static int RemoveNotes(this EventsCollection eventsCollection, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);
            ThrowIfArgument.IsNull(nameof(match), match);

            var tag = new object();
            var notesToRemoveCount = eventsCollection.ProcessNotes(
                n => n.TimedNoteOnEvent.Event.Tag = n.TimedNoteOffEvent.Event.Tag = tag,
                match,
                false);

            if (notesToRemoveCount == 0)
                return 0;

            eventsCollection.RemoveTimedEvents(e => e.Event.Tag == tag);
            return notesToRemoveCount;
        }

        public static int RemoveNotes(this TrackChunk trackChunk)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);

            return trackChunk.RemoveNotes(note => true);
        }

        /// <summary>
        /// Removes all the <see cref="Note"/> that match the conditions defined by the specified predicate.
        /// </summary>
        /// <param name="trackChunk"><see cref="TrackChunk"/> to search for notes to remove.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to remove.</param>
        /// <returns>Count of removed notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trackChunk"/> is <c>null</c>.</exception>
        public static int RemoveNotes(this TrackChunk trackChunk, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);
            ThrowIfArgument.IsNull(nameof(match), match);

            return trackChunk.Events.RemoveNotes(match);
        }

        public static int RemoveNotes(this IEnumerable<TrackChunk> trackChunks)
        {
            ThrowIfArgument.IsNull(nameof(trackChunks), trackChunks);

            return trackChunks.RemoveNotes(note => true);
        }

        /// <summary>
        /// Removes all the <see cref="Note"/> that match the conditions defined by the specified predicate.
        /// </summary>
        /// <param name="trackChunks">Collection of <see cref="TrackChunk"/> to search for notes to remove.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to remove.</param>
        /// <returns>Count of removed notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trackChunks"/> is <c>null</c>.</exception>
        public static int RemoveNotes(this IEnumerable<TrackChunk> trackChunks, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(trackChunks), trackChunks);
            ThrowIfArgument.IsNull(nameof(match), match);

            var tag = new object();
            var notesToRemoveCount = trackChunks.ProcessNotes(
                n => n.TimedNoteOnEvent.Event.Tag = n.TimedNoteOffEvent.Event.Tag = tag,
                match,
                false);

            if (notesToRemoveCount == 0)
                return 0;

            trackChunks.RemoveTimedEvents(e => e.Event.Tag == tag);
            return notesToRemoveCount;
        }

        public static int RemoveNotes(this MidiFile file)
        {
            ThrowIfArgument.IsNull(nameof(file), file);

            return file.RemoveNotes(note => true);
        }

        // TODO: exception in ///
        /// <summary>
        /// Removes all the <see cref="Note"/> that match the conditions defined by the specified predicate.
        /// </summary>
        /// <param name="file"><see cref="MidiFile"/> to search for notes to remove.</param>
        /// <param name="match">The predicate that defines the conditions of the <see cref="Note"/> to remove.</param>
        /// <returns>Count of removed notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
        public static int RemoveNotes(this MidiFile file, Predicate<Note> match)
        {
            ThrowIfArgument.IsNull(nameof(file), file);
            ThrowIfArgument.IsNull(nameof(match), match);

            return file.GetTrackChunks().RemoveNotes(match);
        }

        [Obsolete("OBS9")]
        /// <summary>
        /// Adds collection of notes to the specified <see cref="EventsCollection"/>.
        /// </summary>
        /// <param name="eventsCollection"><see cref="EventsCollection"/> to add notes to.</param>
        /// <param name="notes">Notes to add to the <paramref name="eventsCollection"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="eventsCollection"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="notes"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static void AddNotes(this EventsCollection eventsCollection, IEnumerable<Note> notes)
        {
            ThrowIfArgument.IsNull(nameof(eventsCollection), eventsCollection);
            ThrowIfArgument.IsNull(nameof(notes), notes);

            using (var notesManager = eventsCollection.ManageNotes())
            {
                notesManager.Notes.Add(notes);
            }
        }

        [Obsolete("OBS9")]
        /// <summary>
        /// Adds collection of notes to the specified <see cref="TrackChunk"/>.
        /// </summary>
        /// <param name="trackChunk"><see cref="TrackChunk"/> to add notes to.</param>
        /// <param name="notes">Notes to add to the <paramref name="trackChunk"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <para>One of the following errors occured:</para>
        /// <list type="bullet">
        /// <item>
        /// <description><paramref name="trackChunk"/> is <c>null</c>.</description>
        /// </item>
        /// <item>
        /// <description><paramref name="notes"/> is <c>null</c>.</description>
        /// </item>
        /// </list>
        /// </exception>
        public static void AddNotes(this TrackChunk trackChunk, IEnumerable<Note> notes)
        {
            ThrowIfArgument.IsNull(nameof(trackChunk), trackChunk);
            ThrowIfArgument.IsNull(nameof(notes), notes);

            trackChunk.Events.AddNotes(notes);
        }

        [Obsolete("OBS7")]
        /// <summary>
        /// Creates a track chunk with the specified notes.
        /// </summary>
        /// <param name="notes">Collection of notes to create a track chunk.</param>
        /// <returns><see cref="TrackChunk"/> containing the specified notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="notes"/> is <c>null</c>.</exception>
        public static TrackChunk ToTrackChunk(this IEnumerable<Note> notes)
        {
            ThrowIfArgument.IsNull(nameof(notes), notes);

            return ((IEnumerable<ITimedObject>)notes).ToTrackChunk();
        }

        [Obsolete("OBS8")]
        /// <summary>
        /// Creates a MIDI file with the specified notes.
        /// </summary>
        /// <param name="notes">Collection of notes to create a MIDI file.</param>
        /// <returns><see cref="MidiFile"/> containing the specified notes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="notes"/> is <c>null</c>.</exception>
        public static MidiFile ToFile(this IEnumerable<Note> notes)
        {
            ThrowIfArgument.IsNull(nameof(notes), notes);

            return ((IEnumerable<ITimedObject>)notes).ToFile();
        }

        /// <summary>
        /// Returns <see cref="MusicTheory.Note"/> corresponding to the specified <see cref="Note"/>.
        /// </summary>
        /// <param name="note"><see cref="Note"/> to get music theory note from.</param>
        /// <returns><see cref="MusicTheory.Note"/> corresponding to the <paramref name="note"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="note"/> is <c>null</c>.</exception>
        public static MusicTheory.Note GetMusicTheoryNote(this Note note)
        {
            ThrowIfArgument.IsNull(nameof(note), note);

            return note.UnderlyingNote;
        }

        internal static int ProcessNotes(this IEnumerable<TrackChunk> trackChunks, Action<Note> action, Predicate<Note> match, bool canTimeOrLengthBeChanged)
        {
            var eventsCollections = trackChunks.Where(c => c != null).Select(c => c.Events).ToArray();
            var eventsCount = eventsCollections.Sum(c => c.Count);

            var iMatched = 0;

            var timesChanged = false;
            var lengthsChanged = false;
            var timedEvents = canTimeOrLengthBeChanged ? new List<Tuple<TimedEvent, int>>(eventsCount) : null;

            foreach (var timedObjectTuple in eventsCollections.GetTimedEventsLazy(eventsCount, false).GetNotesAndTimedEventsLazy())
            {
                var note = timedObjectTuple.Item1 as Note;
                if (note != null && match?.Invoke(note) != false)
                {
                    var time = note.Time;
                    var length = note.Length;

                    action(note);

                    timesChanged = note.Time != time;
                    lengthsChanged = note.Length != length;

                    iMatched++;
                }

                if (canTimeOrLengthBeChanged)
                {
                    if (note != null)
                    {
                        timedEvents.Add(Tuple.Create(note.TimedNoteOnEvent, timedObjectTuple.Item2));
                        timedEvents.Add(Tuple.Create(note.TimedNoteOffEvent, timedObjectTuple.Item3));
                    }
                    else
                        timedEvents.Add(Tuple.Create((TimedEvent)timedObjectTuple.Item1, timedObjectTuple.Item2));
                }
            }

            // TODO: unify with timed events managing

            if (timesChanged || lengthsChanged)
            {
                var times = new long[eventsCollections.Length];
                var indices = new int[eventsCollections.Length];

                foreach (var e in timedEvents.OrderBy(e => e.Item1.Time))
                {
                    var midiEvent = e.Item1.Event;
                    midiEvent.DeltaTime = e.Item1.Time - times[e.Item2];
                    eventsCollections[e.Item2][indices[e.Item2]++] = midiEvent;

                    times[e.Item2] = e.Item1.Time;
                }
            }

            return iMatched;
        }

        internal static int ProcessNotes(this EventsCollection eventsCollection, Action<Note> action, Predicate<Note> match, bool canTimeOrLengthBeChanged)
        {
            var iMatched = 0;

            var timesChanged = false;
            var lengthsChanged = false;
            var timedEvents = canTimeOrLengthBeChanged ? new List<TimedEvent>(eventsCollection.Count) : null;

            foreach (var timedObject in eventsCollection.GetTimedEventsLazy(false).GetNotesAndTimedEventsLazy())
            {
                var note = timedObject as Note;
                if (note != null && match?.Invoke(note) != false)
                {
                    var time = note.Time;
                    var length = note.Length;

                    action(note);

                    timesChanged = note.Time != time;
                    lengthsChanged = note.Length != length;

                    iMatched++;
                }

                if (canTimeOrLengthBeChanged)
                {
                    if (note != null)
                    {
                        timedEvents.Add(note.TimedNoteOnEvent);
                        timedEvents.Add(note.TimedNoteOffEvent);
                    }
                    else
                        timedEvents.Add((TimedEvent)timedObject);
                }
            }

            if (timesChanged || lengthsChanged)
            {
                var time = 0L;
                var i = 0;

                foreach (var e in timedEvents.OrderBy(e => e.Time))
                {
                    var midiEvent = e.Event;
                    midiEvent.DeltaTime = e.Time - time;
                    eventsCollection[i++] = midiEvent;

                    time = e.Time;
                }
            }

            return iMatched;
        }

        internal static IEnumerable<Tuple<ITimedObject, int, int>> GetNotesAndTimedEventsLazy(this IEnumerable<Tuple<TimedEvent, int>> timedEvents)
        {
            var objectsDescriptors = new LinkedList<IObjectDescriptorIndexed>();
            var notesDescriptorsNodes = new Dictionary<NoteId, Stack<LinkedListNode<IObjectDescriptorIndexed>>>();

            foreach (var timedEventTuple in timedEvents)
            {
                var timedEvent = timedEventTuple.Item1;
                switch (timedEvent.Event.EventType)
                {
                    case MidiEventType.NoteOn:
                        {
                            var noteId = ((NoteOnEvent)timedEvent.Event).GetNoteId();
                            var node = objectsDescriptors.AddLast(new NoteDescriptorIndexed(noteId, timedEvent, timedEventTuple.Item2));

                            Stack<LinkedListNode<IObjectDescriptorIndexed>> nodesStack;
                            if (!notesDescriptorsNodes.TryGetValue(noteId, out nodesStack))
                                notesDescriptorsNodes.Add(noteId, nodesStack = new Stack<LinkedListNode<IObjectDescriptorIndexed>>(2));

                            nodesStack.Push(node);
                        }
                        break;
                    case MidiEventType.NoteOff:
                        {
                            var noteId = ((NoteOffEvent)timedEvent.Event).GetNoteId();

                            Stack<LinkedListNode<IObjectDescriptorIndexed>> nodesStack;
                            LinkedListNode<IObjectDescriptorIndexed> node;

                            if (!notesDescriptorsNodes.TryGetValue(noteId, out nodesStack) || nodesStack.Count == 0 || (node = nodesStack.Pop()).List == null)
                            {
                                objectsDescriptors.AddLast(new TimedEventDescriptorIndexed(timedEvent, timedEventTuple.Item2));
                                break;
                            }

                            var noteDescriptorIndexed = (NoteDescriptorIndexed)node.Value;
                            noteDescriptorIndexed.NoteOffTimedEvent = timedEvent;
                            noteDescriptorIndexed.NoteOffIndex = timedEventTuple.Item2;

                            var previousNode = node.Previous;
                            if (previousNode != null)
                                break;

                            for (var n = node; n != null;)
                            {
                                if (!n.Value.IsCompleted)
                                    break;

                                yield return n.Value.GetIndexedObject();

                                var next = n.Next;
                                objectsDescriptors.Remove(n);
                                n = next;
                            }
                        }
                        break;
                    default:
                        {
                            if (objectsDescriptors.Count == 0)
                                yield return Tuple.Create((ITimedObject)timedEvent, timedEventTuple.Item2, timedEventTuple.Item2);
                            else
                                objectsDescriptors.AddLast(new TimedEventDescriptorIndexed(timedEvent, timedEventTuple.Item2));
                        }
                        break;
                }
            }

            foreach (var objectDescriptor in objectsDescriptors)
            {
                yield return objectDescriptor.GetIndexedObject();
            }
        }

        internal static IEnumerable<ITimedObject> GetNotesAndTimedEventsLazy(this IEnumerable<TimedEvent> timedEvents)
        {
            var objectsDescriptors = new LinkedList<IObjectDescriptor>();
            var notesDescriptorsNodes = new Dictionary<NoteId, Stack<LinkedListNode<IObjectDescriptor>>>();

            foreach (var timedEvent in timedEvents)
            {
                switch (timedEvent.Event.EventType)
                {
                    case MidiEventType.NoteOn:
                        {
                            var noteId = ((NoteOnEvent)timedEvent.Event).GetNoteId();
                            var node = objectsDescriptors.AddLast(new NoteDescriptor(noteId, timedEvent));

                            Stack<LinkedListNode<IObjectDescriptor>> nodesStack;
                            if (!notesDescriptorsNodes.TryGetValue(noteId, out nodesStack))
                                notesDescriptorsNodes.Add(noteId, nodesStack = new Stack<LinkedListNode<IObjectDescriptor>>(2));

                            nodesStack.Push(node);
                        }
                        break;
                    case MidiEventType.NoteOff:
                        {
                            var noteId = ((NoteOffEvent)timedEvent.Event).GetNoteId();
                            
                            Stack<LinkedListNode<IObjectDescriptor>> nodesStack;
                            LinkedListNode<IObjectDescriptor> node;

                            if (!notesDescriptorsNodes.TryGetValue(noteId, out nodesStack) || nodesStack.Count == 0 || (node = nodesStack.Pop()).List == null)
                            {
                                objectsDescriptors.AddLast(new TimedEventDescriptor(timedEvent));
                                break;
                            }

                            ((NoteDescriptor)node.Value).NoteOffTimedEvent = timedEvent;

                            var previousNode = node.Previous;
                            if (previousNode != null)
                                break;

                            for (var n = node; n != null;)
                            {
                                if (!n.Value.IsCompleted)
                                    break;

                                yield return n.Value.GetObject();

                                var next = n.Next;
                                objectsDescriptors.Remove(n);
                                n = next;
                            }
                        }
                        break;
                    default:
                        {
                            if (objectsDescriptors.Count == 0)
                                yield return timedEvent;
                            else
                                objectsDescriptors.AddLast(new TimedEventDescriptor(timedEvent));
                        }
                        break;
                }
            }

            foreach (var objectDescriptor in objectsDescriptors)
            {
                yield return objectDescriptor.GetObject();
            }
        }

        #endregion
    }
}
