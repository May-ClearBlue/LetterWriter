﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace LetterWriter
{
    public abstract class TextFormatter
    {
        public LineBreakRule LineBreakRule { get; set; }
        public GlyphProvider GlyphProvider { get; set; }

        public TextFormatter()
        {
        }

        protected virtual void Initialize()
        {
            this.LineBreakRule = new LineBreakRule();
            this.GlyphProvider = this.CreateGlyphProvider();
        }

        public abstract GlyphProvider CreateGlyphProvider();

        public abstract TextModifierScope CreateTextModifierScope(TextModifierScope parent, TextModifier textModifier);

        public TextLine FormatLine(TextSource textSource, int paragraphWidth, TextLineBreakState state)
        {
            // TextModifierScopeが未作成
            if (state.TextModifierScope == null)
            {
                state.TextModifierScope = this.CreateTextModifierScope(null, null);
            }

            var ptr = new TextRunPointer()
            {
                TextRunIndex = state.TextRunIndex,
                Position = state.Position,
                TextSource = textSource,
                GlyphIndex = state.GlyphLastIndex,
            };
            ptr.Initialize();

            // もう終わり
            if (ptr.EndOfSource)
            {
                return null;
            }

            var width = 0;
            var glyphs = new List<GlyphPlacement>();
            var stackedModifierScopes = new Stack<TextModifierScope>(); // TextEndOfSegmentでTextModifierScopeが終わったときに積んでおく(禁則で逆方向に戻ることがあるから)

            var spacing = state.TextModifierScope.Spacing ?? 0;
            // 1-2-1 の 1
            var rubySpace = 0f;
            var rubyParantSpace = 0f;

            while (!ptr.EndOfSource)
            {
                // 改行
                if (ptr.Current is LineBreak)
                {
                    ptr.Next();
                    goto BREAK_TEXTLINE;
                }

                // スタイル変更
                if (ptr.Current is TextModifier)
                {
                    state.TextModifierScope = this.CreateTextModifierScope(state.TextModifierScope, (TextModifier)ptr.Current);
                }
                if (ptr.Current is TextEndOfSegment)
                {
                    stackedModifierScopes.Push(state.TextModifierScope);
                    state.TextModifierScope = state.TextModifierScope.Parent;
                }

                // ルビグループ
                if (ptr.Current is TextCharactersRubyGroup)
                {
                    var rubyTextRun = (TextCharactersRubyGroup)ptr.Current;
                    var rubyTextRunGlyphs = rubyTextRun.GetCharacters(this.GlyphProvider, state.TextModifierScope);
                    var rubyTextRunRubyGlyphs = rubyTextRun.GetRubyCharacters(this.GlyphProvider, state.TextModifierScope);

                    // 最初にサイズを計る
                    // 親文字列の幅
                    // TODO: spacing
                    var textRunWidth = rubyTextRunGlyphs.Sum(x => x.AdvanceWidth + (spacing * x.AdvanceWidth));
                    var textRunHeight = rubyTextRunGlyphs.Select(x => x.Height).DefaultIfEmpty().Max(); /* lineSpacing */
                    // ルビ文字列の幅
                    // TODO: spacing
                    var textRunRubyWidth = rubyTextRunRubyGlyphs.Sum(x => x.AdvanceWidth /* + rubySpacing */);

                    // 入りきらなくて行頭ではない場合には次の行に送る(ルビグループの途中で改行できない)
                    if (width != 0 && paragraphWidth < width + Math.Max(textRunWidth, textRunRubyWidth))
                    {
                        goto BREAK_TEXTLINE;
                    }

                    // ルビ文字列と親文字列の長さによって配置を決める
                    if (textRunWidth > textRunRubyWidth)
                    {
                        // 親文字列のほうが長い
                        // ルビを1-2-1ルールで並べる
                        var spaces = textRunWidth - textRunRubyWidth;
                        rubySpace = spaces / (rubyTextRunRubyGlyphs.Length * 2.0f);
                    }
                    else
                    {
                        // ルビの文字列のほうが長い
                        // 親文字列を1-2-1ルールで並べる
                        var spaces = textRunRubyWidth - textRunWidth;
                        rubyParantSpace = spaces / (rubyTextRunGlyphs.Length * 2.0f);
                    }

                    // 親文字列を置く前にルビ文字を置いてしまう
                    var rubyWidth = width; // 開始位置は親文字を基準にする
                    var rubyIndex = 0f;
                    var rubyIndexStep = rubyTextRunGlyphs.Length / (float)rubyTextRunRubyGlyphs.Length;
                    foreach (var glyph in rubyTextRunRubyGlyphs)
                    {
                        glyphs.Add(new GlyphPlacement(glyph, (int)(rubyWidth + rubySpace), -textRunHeight, ptr.GlyphIndex + (int)Math.Ceiling(rubyIndex += rubyIndexStep) - 1));

                        // TODO: spacing
                        rubyWidth = rubyWidth + glyph.AdvanceWidth + (int)(rubySpace * 2) /* + spacing */;
                    }
                }
                else
                {
                    rubyParantSpace = 0;
                    rubySpace = 0;
                }

                // 一文字ずつ置いていく
                var textRunGlyphs = ptr.Current.GetCharacters(this.GlyphProvider, state.TextModifierScope);
                while (true)
                {
                    if (ptr.Next())
                        break;

                    if (textRunGlyphs.Length == 0)
                        break;

                    var glyph = textRunGlyphs[ptr.Position];
                    var nextWidth = width + glyph.AdvanceWidth + (int)(rubyParantSpace * 2) + (int)(spacing * glyph.AdvanceWidth); // TODO: spacing

                    // 行頭で空白文字はスキップする
                    if (!glyphs.Any() && glyph.IsWhiteSpaceOrControl)
                    {
                        glyphs.Add(GlyphPlacement.Empty); // 空の文字を置いておかないと折り返しで巻き戻されたときに困る
                        continue;
                    }

                    // 行に入りきらない場合で折り返し許可
                    // 1文字以上あるときに限る(1文字も入らないと無限に空っぽになる)
                    if (nextWidth > paragraphWidth && ptr.Current.CanWrap && glyphs.Count > 0)
                    {
                        // 一文字戻す
                        // この時点ではまだGlyphsに入れていないので巻き戻すときに削る必要はない
                        var nextGlyphBeginOfLine = glyph;
                        if (ptr.Back())
                        {
                            // 巻き戻してTextModifierが出てきたときはScopeも戻す
                            if (ptr.Current is TextModifier)
                            {
                                state.TextModifierScope = state.TextModifierScope.Parent;
                            }
                            // 巻き戻してTextEndOfSegmentが出てきたときはScopeの状態を戻す
                            if (ptr.Current is TextEndOfSegment)
                            {
                                state.TextModifierScope = stackedModifierScopes.Pop();
                            }
                        }
                        else
                        {
                            // 前の文字

                            // 次の行の開始となる文字(=現在 glyph に入ってるもの)が禁則文字の場合にはさらに巻き戻して前の文字を追い出す必要がある
                            // ただし1文字以上は含まれていてほしいので2文字以上のときだけ。
                            while (!this.LineBreakRule.CanWrap(nextGlyphBeginOfLine) && ptr.Current.CanWrap && glyphs.Count > 1)
                            {
                                // 前の文字 or TextRunに
                                if (ptr.Back())
                                {
                                    // 巻き戻してTextModifierが出てきたときはScopeも戻す
                                    if (ptr.Current is TextModifier)
                                    {
                                        state.TextModifierScope = state.TextModifierScope.Parent;
                                    }
                                    // 巻き戻してTextEndOfSegmentが出てきたときはScopeの状態を戻す
                                    if (ptr.Current is TextEndOfSegment)
                                    {
                                        state.TextModifierScope = stackedModifierScopes.Pop();
                                    }

                                    // ルビグループは途中改行できないのでルビグループごと次の行へ送る
                                    // ただし行の始まりがこのルビグループだったらあきらめる
                                    if (!ptr.Current.CanWrap)
                                    {
                                        var len = ptr.Current.TotalGlyphCount;
                                        if (glyphs.Count - len > 0)
                                        {
                                            // TextRunの分丸ごと消す
                                            glyphs.RemoveRange((glyphs.Count - len), len);
                                            nextGlyphBeginOfLine = glyphs.Last().Glyph;

                                            // 前のTextRunの最後を指すようにする
                                            ptr.BackRun();
                                            continue;
                                        }
                                        else
                                        {
                                            // 1文字も残らない(=TextRunが開始地点)ので消せないで終わる
                                            // 戻さなかったことにして次の行から始まるようにする
                                            ptr.NextRun();
                                            goto BREAK_TEXTLINE;
                                        }
                                    }
                                }
                                else
                                {
                                    var lastPlacedGlyph = glyphs.Last();
                                    glyphs.Remove(lastPlacedGlyph);

                                    nextGlyphBeginOfLine = lastPlacedGlyph.Glyph;
                                }
                            }
                        }

                        goto BREAK_TEXTLINE;
                    }

                    glyphs.Add(new GlyphPlacement(glyph, (int)(width + rubyParantSpace), 0, ptr.GlyphIndex++));

                    width = nextWidth;
                }
            }

            // 行末
            // 次の行へ続くための情報を書き戻す
            BREAK_TEXTLINE:

            // 行末禁則
            if (glyphs.Count > 1)
            {
                // 行末が禁則文字だったら追い出すために巻き戻す必要がある
                // ただし1文字以上は含まれていてほしいので2文字以上のときだけ。
                while (true)
                {
                    var glyphPlacement = glyphs.Last();

                    if (!this.LineBreakRule.IsLineBreakRuleNotAllowedAtEndOfLine(glyphPlacement.Glyph) ||
                        !ptr.Previous.CanWrap ||
                        glyphs.Count < 2)
                    {
                        break;
                    }

                    // 前のTextRunに
                    if (ptr.Back())
                    {
                        // 巻き戻してTextModifierが出てきたときはScopeも戻す
                        if (ptr.Current is TextModifier)
                        {
                            state.TextModifierScope = state.TextModifierScope.Parent;
                        }
                        // 巻き戻してTextEndOfSegmentが出てきたときはScopeの状態を戻す
                        if (ptr.Current is TextEndOfSegment)
                        {
                            state.TextModifierScope = stackedModifierScopes.Pop();
                        }

                        // ルビグループは途中改行できないのでルビグループごと次の行へ送る
                        // ただし行の始まりがこのルビグループだったらあきらめる
                        if (!ptr.Current.CanWrap)
                        {
                            var len = ptr.Current.Length;
                            if (glyphs.Count - len > 0)
                            {
                                // TextRunの分丸ごと消す
                                glyphs.RemoveRange(((glyphs.Count - 1) - len), len);
                                continue;
                            }
                            else
                            {
                                // 1文字も残らない(=TextRunが開始地点)ので消せないで終わる
                                ptr.NextRun();
                                goto BREAK_TEXTLINE;
                            }
                        }
                    }
                    else
                    {
                        glyphs.Remove(glyphPlacement);
                    }
                }
            }

            state.Position = ptr.Position;
            state.TextRunIndex = ptr.TextRunIndex;
            state.GlyphLastIndex = ptr.GlyphIndex;
            return new TextLine()
            {
                PlacedGlyphs = glyphs.Where(x => x != GlyphPlacement.Empty).ToArray(),
            };
        }

        private struct TextRunPointer
        {
            public int GlyphIndex { get; set; }
            public int Position { get; set; }
            public int TextRunIndex { get; set; }

            public TextSource TextSource { get; set; }

            public TextRun Current { get; private set; }

            public TextRun Previous
            {
                get
                {
                    return (this.Position == -1)
                        ? this.TextSource.TextRuns[this.TextRunIndex - 1]
                        : this.TextSource.TextRuns[this.TextRunIndex];
                }
            }

            public bool CanRead
            {
                get { return !this.EndOfSource && (this.Position < this.Current.Length); }
            }

            public bool EndOfSource
            {
                get { return (this.TextRunIndex == this.TextSource.TextRuns.Length); }
            }

            public void Initialize()
            {
                this.UpdateCurrent();
            }

            private void UpdateCurrent()
            {
                if (this.TextRunIndex > this.TextSource.TextRuns.Length - 1)
                {
                    this.Current = null;
                }
                else
                {
                    this.Current = this.TextSource.TextRuns[this.TextRunIndex]; // TextRunIndex は最初0から始まってNext呼べるように1オリジン
                }
            }

            public void BackRun()
            {
                this.TextRunIndex--;
                this.UpdateCurrent();
                this.Position = this.Current.Length - 1;
            }

            public void NextRun()
            {
                this.TextRunIndex++;
                this.UpdateCurrent();
                this.Position = -1;
            }

            public bool Back()
            {
                this.Position--;

                if (this.Position < -1)
                {
                    this.BackRun();
                    return true;
                }

                return false;
            }

            public bool Next()
            {
                if (this.EndOfSource) throw new InvalidOperationException();

                this.Position++;

                if (this.Position > this.Current.Length - 1)
                {
                    this.NextRun();
                    return true;
                }

                return false;
            }
        }
    }
}