﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZenithEngine
{
    public class MidiFile : IDisposable
    {
        Stream MidiFileReader;
        public ushort division;
        public int trackcount;
        public ushort format;

        public int zerothTempo = 500000;

        List<long> trackBeginnings = new List<long>();
        List<uint> trackLengths = new List<uint>();

        public MidiTrack[] tracks;

        public MidiInfo info;

        public long maxTrackTime;
        public long noteCount = 0;

        public long currentSyncTime = 0;
        // public double currentFlexSyncTime = 0;

        public FastList<Note> globalDisplayNotes = new FastList<Note>();
        public FastList<Tempo> globalTempoEvents = new FastList<Tempo>();
        public FastList<ColorChange> globalColorEvents = new FastList<ColorChange>();
        public FastList<PlaybackEvent> globalPlaybackEvents = new FastList<PlaybackEvent>();

        public long lastTempoTick = 0;
        public double lastTempoTime = 0;

        public double tempoTickMultiplier = 0;

        public int unendedTracks = 0;

        public bool loadedMidi = false;

        RenderSettings settings;

        public MidiFile(string filename, RenderSettings settings)
        {
            this.settings = settings;
            MidiFileReader = new StreamReader(filename).BaseStream;
            ParseHeaderChunk();
            while (MidiFileReader.Position < MidiFileReader.Length)
            {
                ParseTrackChunk();
            }
            tracks = new MidiTrack[trackcount];

            Console.WriteLine("将音轨读入内存...");
            info = new MidiInfo();
            LoadAndParseAll(true);
            Console.WriteLine("读取完成");
            Console.WriteLine("音符总数: " + noteCount);
            unendedTracks = trackcount;

            info.division = division;
            info.firstTempo = zerothTempo;
            info.noteCount = noteCount;
            info.tickLength = maxTrackTime;
            info.trackCount = trackcount;

            lastTempoTick = 0;
            lastTempoTime = 0;

            tempoTickMultiplier = (double)division / 500000 * 1000;
        }

        void AssertText(string text)
        {
            foreach (char c in text)
            {
                if (MidiFileReader.ReadByte() != c)
                {
                    throw new Exception("Corrupt chunk headers");
                }
            }
        }

        uint ReadInt32()
        {
            uint length = 0;
            length = (length << 8) | (byte)MidiFileReader.ReadByte();
            length = (length << 8) | (byte)MidiFileReader.ReadByte();
            length = (length << 8) | (byte)MidiFileReader.ReadByte();
            length = (length << 8) | (byte)MidiFileReader.ReadByte();
            return length;
        }

        ushort ReadInt16()
        {
            ushort length = 0;
            length = (ushort)((length << 8) | (byte)MidiFileReader.ReadByte());
            length = (ushort)((length << 8) | (byte)MidiFileReader.ReadByte());
            return length;
        }

        void ParseHeaderChunk()
        {
            AssertText("MThd");
            uint length = ReadInt32();
            if (length != 6) throw new Exception("Header chunk size isn't 6");
            format = ReadInt16();
            ReadInt16();
            division = ReadInt16();
            if (format == 2) throw new Exception("Midi type 2 not supported");
            if (division < 0) throw new Exception("Division < 0 not supported");
        }

        void ParseTrackChunk()
        {
            AssertText("MTrk");
            uint length = ReadInt32();
            trackBeginnings.Add(MidiFileReader.Position);
            trackLengths.Add(length);
            MidiFileReader.Position += length;
            trackcount++;
            Console.WriteLine("音轨 " + trackcount + ", 长度 " + length);
        }


        public bool ParseUpTo(/*double*/long targetTime)
        {
            if (settings.timeBasedNotes) targetTime = (long)((targetTime - lastTempoTime) * tempoTickMultiplier + lastTempoTick);
            lock (globalDisplayNotes)
            {
                /*if (settings.timeBasedNotes)
                    for (; currentFlexSyncTime <= targetTime && settings.running; currentSyncTime++)
                    {
                        currentFlexSyncTime += 1 / tempoTickMultiplier;
                        int ut = 0;
                        /*for (int trk = 0; trk < trackcount; trk++)
                        {
                            var t = tracks[trk];
                            if (!t.trackEnded)
                            {
                                ut++;
                                t.Step(currentSyncTime);
                            }
                        }
                        unendedTracks = ut;
                        * /
                        foreach (MidiTrack trk in tracks)
                        {
                            if (!trk.trackEnded)
                            {
                                ut++;
                                trk.Step(currentSyncTime);
                            }
                        }
                    }
                else
                    for (; currentSyncTime <= targetTime && settings.running; currentSyncTime++)
                    {
                        int ut = 0;
                        /*for (int trk = 0; trk < trackcount; trk++)
                        {
                            var t = tracks[trk];
                            if (!t.trackEnded)
                            {
                                ut++;
                                t.Step(currentSyncTime);
                            }
                        }* /
                        // foreach may better
                        foreach (MidiTrack trk in tracks)
                        {
                            if (!trk.trackEnded)
                            {
                                ut++;
                                trk.Step(currentSyncTime);
                            }
                        }
                        unendedTracks = ut;
                    }
                foreach (MidiTrack t in tracks)
                {
                    if (!t.trackEnded) return true;
                }
                return false;*/
                int ut;
                for (; currentSyncTime <= targetTime && settings.running; ++currentSyncTime)
                {
                    ut = 0;
                    foreach (var t in tracks)
                    {
                        if (!t.trackEnded)
                        {
                            ++ut;
                            t.Step(currentSyncTime);
                        }
                    }
                    unendedTracks = ut;
                }
                foreach (var t in tracks)
                {
                    if (!t.trackEnded) return true;
                }
                return false;
            }
        }

        public void LoadAndParseAll(bool useBufferStream = false)
        {
            long[] tracklens = new long[tracks.Length];
            int p = 0;
            List<FastList<Tempo>> tempos = new List<FastList<Tempo>>();
            Parallel.For(0, tracks.Length, (i) =>
               {
                   var reader = new BufferByteReader(MidiFileReader, settings.maxTrackBufferSize, trackBeginnings[i], trackLengths[i]);
                   tracks[i] = new MidiTrack(i, reader, this, settings);
                   var t = tracks[i];
                   while (!t.trackEnded)
                   {
                       try
                       {
                           t.ParseNextEventFast();
                       }
                       catch
                       {
                           break;
                       }
                   }
                   noteCount += t.noteCount;
                   tracklens[i] = t.trackTime;
                   if (t.foundTimeSig != null)
                       info.timeSig = t.foundTimeSig;
                   if (t.zerothTempo != -1)
                   {
                       zerothTempo = t.zerothTempo;
                   }
                   lock (tempos) tempos.Add(t.TempoEvents);
                   t.Reset();
                   Console.WriteLine("已加载轨道 " + ++p + "/" + tracks.Length);
                   GC.Collect();
               });
            maxTrackTime = tracklens.Max();
            Console.WriteLine("正在处理节拍");
            LinkedList<Tempo> Tempos = new LinkedList<Tempo>();
            var iters = tempos.Select(t => t.GetEnumerator()).ToArray();
            bool[] unended = new bool[iters.Length];
            for (int i = 0; i < iters.Length; i++) unended[i] = iters[i].MoveNext();
            while (true)
            {
                long smallest = 0;
                bool first = true;
                int id = 0;
                for (int i = 0; i < iters.Length; ++i)
                {
                    if (!unended[i]) continue;
                    if (first)
                    {
                        smallest = iters[i].Current.pos;
                        id = i;
                        first = false;
                        continue;
                    }
                    if (iters[i].Current.pos < smallest)
                    {
                        smallest = iters[i].Current.pos;
                        id = i;
                    }
                }
                if (first)
                {
                    break;
                }
                Tempos.AddLast(iters[id].Current);
                unended[id] = iters[id].MoveNext();
            }

            double time = 0;
            long ticks = maxTrackTime;
            double multiplier = (double)500000 / division / 1000000;
            long lastt = 0;
            foreach (var t in Tempos)
            {
                var offset = t.pos - lastt;
                time += offset * multiplier;
                ticks -= offset;
                lastt = t.pos;
                multiplier = (double)t.tempo / division / 1000000;
            }

            time += ticks * multiplier;

            info.secondsLength = time;

            maxTrackTime = tracklens.Max();
            unendedTracks = trackcount;
            // loaded midi
            loadedMidi = true;
        }

        public void SetZeroColors()
        {
            foreach (var t in tracks) t.SetZeroColors();
        }

        public void Reset()
        {
            globalDisplayNotes.Unlink();
            globalTempoEvents.Unlink();
            globalColorEvents.Unlink();
            globalPlaybackEvents.Unlink();
            currentSyncTime = 0;
            // currentFlexSyncTime = 0;
            unendedTracks = trackcount;
            tempoTickMultiplier = (double)division / 500000 * 1000;
            // foreach (var t in tracks) t.Reset();
            Parallel.ForEach(tracks, t =>
            {
                t.Reset();
            });
        }

        public void Dispose()
        {
            // foreach (var t in tracks) t.Dispose();
            Parallel.ForEach(tracks, t =>
            {
                t.Dispose();
            });
            MidiFileReader.Dispose();
            loadedMidi = false;
        }
    }
}
