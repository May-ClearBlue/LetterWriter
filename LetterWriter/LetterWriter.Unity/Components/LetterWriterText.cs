﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using LetterWriter;
using LetterWriter.Markup;
using LetterWriter.Unity.Markup;

namespace LetterWriter.Unity.Components
{
    public class LetterWriterText : MaskableGraphic, ILayoutElement
    {
        private bool _requireReformatText = true;
        private Rect _previousRect = Rect.MinMaxRect(0, 0, 0, 0);
        private TextLine[] _formattedTextLines;
        private TextSource _textSource;
        private string _prevText; // TODO: 変更チェックが雑なのであとで直す

        private LetterWriterMarkupParser _markupParser;
        private TextFormatter _textFormatter;

        private RectTransform _cachedRectTransform;
        public RectTransform CachedRectTransform
        {
            get { return this._cachedRectTransform ?? (this._cachedRectTransform = this.GetComponent<RectTransform>()); }
        }

        public new Color color { get { return base.color; } set { base.color = value; this.MarkAsReformatRequired(); } }

        [SerializeField]
        private LetterWriterExtensibilityProvider _extensibilityProvider;
        public LetterWriterExtensibilityProvider ExtensibilityProvider
        {
            get { return this._extensibilityProvider; }
            set { this._extensibilityProvider = value; }
        }

        [SerializeField]
        private Font _font;
        public Font Font
        {
            get { return this._font; }
            set { this._font = value; this.MarkAsReformatRequired(); }
        }

        [SerializeField]
        [Multiline]
        private string _text;
        public string Text
        {
            get { return this._text; }
            set { this._text = value; this.RefreshTextSourceIfNeeded(); this.MarkAsReformatRequired(); }
        }

        [SerializeField]
        private int _fontSize = 24;
        public int FontSize
        {
            get { return this._fontSize; }
            set { this._fontSize = value; this.MarkAsReformatRequired(); }
        }

        [SerializeField]
        private bool _isLineHeightFixed = false;
        /// <summary>
        /// 一行の高さを固定値とするかどうかを取得、設定します。設定が有効な場合には行の高さはLineHeightに従うようになります。
        /// </summary>
        public bool IsLineHeightFixed
        {
            get { return this._isLineHeightFixed; }
            set { this._isLineHeightFixed = value; this.MarkAsRebuildRequired(); }
        }

        [SerializeField]
        private float _lineHeight = 1;
        /// <summary>
        /// 一行の高さを取得、設定します。1はフォントの高さと同等です。
        /// </summary>
        public float LineHeight
        {
            get { return this._lineHeight; }
            set { this._lineHeight = value; this.MarkAsReformatRequired(); }
        }

        /// <summary>
        /// 表示される長さを取得、設定します。
        /// </summary>
        [SerializeField]
        private int _visibleLength = -1;
        public int VisibleLength
        {
            get { return this._visibleLength; }
            set
            {
                this._visibleLength = value;
                this.MarkAsRebuildRequired();
            }
        }

        private int _maxIndex;
        /// <summary>
        /// テキストの長さに対する最大のインデックスの値を取得します。
        /// </summary>
        public int MaxIndex
        {
            get { return this._maxIndex; }
        }

        [SerializeField]
        private bool _isHeightDependingOnVisibleLength = false;
        /// <summary>
        /// 要素の高さが<see cref="VisibleLength" />プロパティに依存するかどうかを取得、設定します。
        /// </summary>
        public bool IsHeightDepenedingOnVisibleLength
        {
            get { return this._isHeightDependingOnVisibleLength; }
            set
            {
                this._isHeightDependingOnVisibleLength = value;
                this.MarkAsReformatRequired();
            }
        }

        [SerializeField]
        private HorizontalWrapMode _horizontalOverflow = HorizontalWrapMode.Wrap;
        /// <summary>
        /// 横方向に対してのオーバーフローの制御を取得、設定します。
        /// </summary>
        public HorizontalWrapMode HorizontalOverflow
        {
            get { return this._horizontalOverflow; }
            set { this._horizontalOverflow = value; this.MarkAsReformatRequired(); }
        }

        [SerializeField]
        private VerticalWrapMode _verticalOverflow = VerticalWrapMode.Overflow;
        /// <summary>
        /// 縦方向に対してのオーバーフローの制御を取得、設定します。
        /// </summary>
        public VerticalWrapMode VerticalOverflow
        {
            get { return this._verticalOverflow; }
            set { this._verticalOverflow = value; this.MarkAsReformatRequired(); }
        }

        [SerializeField]
        private bool _treatNewLineAsLineBreak = true;
        /// <summary>
        /// 改行文字を改行として扱うかどうかを取得、設定します。
        /// </summary>
        public bool TreatNewLineAsLineBreak
        {
            get { return this._treatNewLineAsLineBreak; }
            set { this._treatNewLineAsLineBreak = value; this.RefreshTextSourceIfNeeded(forceUpdate: true); this.MarkAsReformatRequired(); }
        }

        public override Texture mainTexture
        {
            get
            {
                if (this.Font != null && this.Font.material != null && this.Font.material.mainTexture != null)
                    return this.Font.material.mainTexture;

                if (this.m_Material != null)
                    return this.m_Material.mainTexture;

                return base.mainTexture;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            this.MarkAsReformatRequired();
            Font.textureRebuilt += OnFontTextureRebuilt;

            this._cachedRectTransform = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            Font.textureRebuilt -= OnFontTextureRebuilt;
            this._cachedRectTransform = null;
        }

        protected virtual void OnFontTextureRebuilt(Font font)
        {
            this.MarkAsReformatRequired(); // 現状ではフォーマット済みテキストにGlyph位置が含まれているのでテクスチャが変わったら再フォーマットが必要
            this.MarkAsRebuildRequired();
        }

        /// <summary>
        /// テキストソース(マークアップパース結果)の更新が必要であれば行います。
        /// </summary>
        protected void RefreshTextSourceIfNeeded(bool forceUpdate = false)
        {
            if (this._prevText != this._text ||
                this._textSource == null ||
                this._markupParser == null ||
                this._markupParser.TreatNewLineAsLineBreak != this.TreatNewLineAsLineBreak ||
                forceUpdate)
            {
                this._markupParser = this._markupParser ?? this.CreateMarkupParser();
                this._markupParser.TreatNewLineAsLineBreak = this.TreatNewLineAsLineBreak;
                this._textSource = _markupParser.Parse(this.Text);
                this._prevText = this._text;
                this._visibleLength = -1;

                // フォーマット済みテキストも更新するお
                this.UpdateFormattedTextLines();
            }
        }

        /// <summary>
        /// Graphicsの再構築を要求します。再フォーマットは要求しません。
        /// </summary>
        public void MarkAsRebuildRequired()
        {
            this.SetVerticesDirty();
        }

        /// <summary>
        /// パース済みでフォーマットされた内部テキスト情報の再フォーマットを要求します。
        /// </summary>
        public void MarkAsReformatRequired()
        {
            this._requireReformatText = true;
            this.MarkAsRebuildRequired();
        }

        /// <summary>
        /// 内部のフォーマット済みテキストを更新します。
        /// </summary>
        protected virtual void UpdateFormattedTextLines()
        {
            this._formattedTextLines = this.FormatText((this.HorizontalOverflow == HorizontalWrapMode.Wrap) ? this.rectTransform.rect.width : 99999f);
            this._requireReformatText = false;
        }

        /// <summary>
        /// テキストを解析して指定した幅に収まるフォーマット済みテキストを取得します。
        /// </summary>
        /// <param name="width"></param>
        /// <returns></returns>
        protected virtual TextLine[] FormatText(float width)
        {
            this.RefreshTextSourceIfNeeded();

            var textLineBreakState = new TextLineBreakState();
            this._textFormatter = this._textFormatter ?? this.CreateTextFormatter();

            var textLines = new List<TextLine>();
            while (true)
            {
                var textLine = this._textFormatter.FormatLine(this._textSource, (int)width, textLineBreakState);
                if (textLine == null)
                    break;

                textLines.Add(textLine);
            }

            var textLinesArray = textLines.ToArray();
            this._maxIndex = textLinesArray.SelectMany(x => x.PlacedGlyphs.Select(y => y.Index)).DefaultIfEmpty().Max();

            return textLinesArray;
        }

        /// <summary>
        /// <see cref="LetterWriterMarkupParser"/> を生成します。
        /// </summary>
        /// <returns></returns>
        protected virtual LetterWriterMarkupParser CreateMarkupParser()
        {
            if (this.ExtensibilityProvider != null)
            {
                return this.ExtensibilityProvider.CreateMarkupParser();
            }

            return new LetterWriterMarkupParser();
        }

        /// <summary>
        /// <see cref="TextFormatter"/> を生成します。
        /// </summary>
        /// <returns></returns>
        protected virtual TextFormatter CreateTextFormatter()
        {
            if (this.ExtensibilityProvider != null)
            {
                return this.ExtensibilityProvider.CreateTextFormatter(this.Font, this.FontSize, this.color);
            }

            return new UnityTextFormatter(this.Font, this.FontSize, this.color);
        }

#if UNITY_5 && UNITY_5_2
        protected override void OnPopulateMesh(Mesh m)
        {
            if (this.Font == null)
                return;

            // 何か変化があったら再フォーマットする
            if (this._requireReformatText || this._previousRect != this.CachedRectTransform.rect)
            {
                this.UpdateFormattedTextLines();
                this._previousRect = this.CachedRectTransform.rect;
            }

            // 開始位置
            var x = this.rectTransform.rect.xMin;
            var y = this.rectTransform.rect.yMax;

            var leadingBase = ((this.Font.lineHeight - (float)this.Font.fontSize) / this.Font.fontSize) / 2;

            y += (leadingBase * this.FontSize); // 一行目の分、少し上に上げておく

            m.Clear();

            using (var vertexHelper = new VertexHelper(m))
            { 
                foreach (var textLine in this._formattedTextLines)
                {
                    var lineHeight = (this.FontSize + (leadingBase * this.FontSize));

                    // 上にLineHeight-1の半分の空き
                    y -= (lineHeight * (this.LineHeight - 1)) / 2;

                    // 行の高さが固定ではない場合には、上に突き抜けてる分を計算してあげる必要がある
                    if (!this.IsLineHeightFixed)
                    {
                        // 展開するんじゃもん…
                        //lineHeight += textLine.PlacedGlyphs.Where(p => p.Y < 0).Select(p => p.Glyph.Height).DefaultIfEmpty().Max();

                        var max = 0;
                        for (var i = 0; i < textLine.PlacedGlyphs.Length; i++)
                        {
                            var p = textLine.PlacedGlyphs[i];
                            if (p.Y < 0 && p.Glyph.Height > max)
                            {
                                max = p.Glyph.Height;
                            }
                        }
                        lineHeight += max;
                    }

                    // オーバーフロー
                    if (this.VerticalOverflow == VerticalWrapMode.Truncate && this.rectTransform.rect.yMin > (y - lineHeight))
                    {
                        break;
                    }

                    // ここも foreach + Where とかじゃなくて展開するっぽい
                    for (var i = 0; i < textLine.PlacedGlyphs.Length; i++)
                    {
                        var placedGlyph = textLine.PlacedGlyphs[i];
                        if (placedGlyph != GlyphPlacement.Empty &&
                            (this._visibleLength == -1 || placedGlyph.Index < this._visibleLength))
                        {
                            var glyph = (UnityGlyph)placedGlyph.Glyph;
                            var uiVertexes = glyph.BaseVertices;

                            uiVertexes[0].position.x += placedGlyph.X + x;
                            uiVertexes[0].position.y += -placedGlyph.Y + y - lineHeight;

                            uiVertexes[1].position.x += placedGlyph.X + x;
                            uiVertexes[1].position.y += -placedGlyph.Y + y - lineHeight;

                            uiVertexes[2].position.x += placedGlyph.X + x;
                            uiVertexes[2].position.y += -placedGlyph.Y + y - lineHeight;

                            uiVertexes[3].position.x += placedGlyph.X + x;
                            uiVertexes[3].position.y += -placedGlyph.Y + y - lineHeight;

                            vertexHelper.AddUIVertexQuad(uiVertexes);
                        }
                    }

                    // 1行分下に進めて、さらにLineHeight-1の半分の空きを足す
                    y -= (lineHeight * (1 + ((this.LineHeight - 1) / 2)));
                }

                vertexHelper.FillMesh(m);
            }
        }
#endif
#if UNITY_5 && !UNITY_5_2
        protected override void OnFillVBO(List<UIVertex> vbo)
        {
            if (this.Font == null)
                return;

            // 何か変化があったら再フォーマットする
            if (this._requireReformatText || this._previousRect != this.CachedRectTransform.rect)
            {
                this.UpdateText();
                this._requireReformatText = false;
            }

            // 開始位置
            var x = this.rectTransform.rect.xMin;
            var y = this.rectTransform.rect.yMax;

            var leadingBase = ((this.Font.lineHeight - (float) this.Font.fontSize)/this.Font.fontSize) / 2;

            y += (leadingBase*this.FontSize); // 一行目の分、少し上に上げておく

            foreach (var textLine in this._formattedTextLines)
            {
                var lineHeight = (this.FontSize + (leadingBase * this.FontSize));

                // 上にLineHeight-1の半分の空き
                y -= (lineHeight * (this.LineHeight - 1)) / 2;

                // 行の高さが固定ではない場合には、上に突き抜けてる分を計算してあげる必要がある
                if (!this.IsLineHeightFixed)
                {
                    // 展開するんじゃもん…
                    //lineHeight += textLine.PlacedGlyphs.Where(p => p.Y < 0).Select(p => p.Glyph.Height).DefaultIfEmpty().Max();

                    var max = 0;
                    for (var i = 0; i < textLine.PlacedGlyphs.Length; i++)
                    {
                        var p = textLine.PlacedGlyphs[i];
                        if (p.Y < 0 && p.Glyph.Height > max)
                        {
                            max = p.Glyph.Height;
                        }
                    }
                    lineHeight += max;
                }

                // オーバーフロー
                if (this.VerticalOverflow == VerticalWrapMode.Truncate && this.rectTransform.rect.yMin > (y - lineHeight))
                {
                    break;
                }

                // ここも foreach + Where とかじゃなくて展開するっぽい
                for (var i = 0; i < textLine.PlacedGlyphs.Length; i++)
                {
                    var placedGlyph = textLine.PlacedGlyphs[i];
                    if (placedGlyph != GlyphPlacement.Empty &&
                        (this._visibleLength == -1 || placedGlyph.Index < this._visibleLength))
                    {
                        var glyph = (UnityGlyph)placedGlyph.Glyph;
                        var uiVertexes = glyph.BaseVertices;

                        uiVertexes[0].position.x += placedGlyph.X + x;
                        uiVertexes[0].position.y += -placedGlyph.Y + y - lineHeight;

                        uiVertexes[1].position.x += placedGlyph.X + x;
                        uiVertexes[1].position.y += -placedGlyph.Y + y - lineHeight;

                        uiVertexes[2].position.x += placedGlyph.X + x;
                        uiVertexes[2].position.y += -placedGlyph.Y + y - lineHeight;

                        uiVertexes[3].position.x += placedGlyph.X + x;
                        uiVertexes[3].position.y += -placedGlyph.Y + y - lineHeight;

                        vbo.AddRange(uiVertexes);
                    }
                }

                // 1行分下に進めて、さらにLineHeight-1の半分の空きを足す
                y -= (lineHeight * (1 + ((this.LineHeight - 1) / 2)));
            }
        }
#endif

#if UNITY_EDITOR
        protected override void Reset()
        {
            this.Font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            this._textFormatter = null;
            this._markupParser = null;
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            this._textFormatter = null;
            this._markupParser = null;

            this.RefreshTextSourceIfNeeded();
            this.MarkAsReformatRequired();
        }
#endif

        #region ILayoutElement Implementation

        public void CalculateLayoutInputHorizontal()
        {
        }

        public void CalculateLayoutInputVertical()
        {
        }

        public float minHeight { get { return 0; } }
        public float minWidth { get { return 0; } }

        /// <summary>
        /// 幅に対する制限がないものとして最大の横幅(1行の最大長)
        /// </summary>
        public float preferredWidth
        {
            get
            {
                return this.GetPreferredWidth(this.rectTransform.rect.height);
            }
        }
        public float flexibleWidth { get { return -1; } }

        /// <summary>
        /// 現在の幅を固定した想定で必要となる高さ(折り返しも含まれる)
        /// </summary>
        public float preferredHeight
        {
            get
            {
                return this.GetPreferredHeight(this.rectTransform.rect.width);
            }
        }
        public float flexibleHeight { get { return -1; } }
        public int layoutPriority { get { return 0; } }

#endregion

        /// <summary>
        /// 指定した高さに文章が収まる最小の幅を算出します。
        /// </summary>
        /// <param name="constraintHeight"></param>
        /// <returns></returns>
        public float GetPreferredWidth(float constraintHeight)
        {
            var formattedLines = this.FormatText(99999f); // 超巨大ということにしておく
            var firstLine = formattedLines.FirstOrDefault();
            if (firstLine == null) return 0;

            return firstLine.PlacedGlyphs.DefaultIfEmpty().Max(x => x.X + x.Glyph.AdvanceWidth);
        }

        /// <summary>
        /// 指定した幅に文章が収まる最小の高さを算出します。
        /// </summary>
        /// <param name="constraintWidth"></param>
        /// <returns></returns>

        public float GetPreferredHeight(float constraintWidth)
        {
            if (constraintWidth < 0)
                return 0;

            var formattedLines = this.FormatText(constraintWidth);
            var height = 0f;
            var leadingBase = ((this.Font.lineHeight - (float)this.Font.fontSize) / this.Font.fontSize) / 2;

            height -= (leadingBase * this.FontSize); // 一行目の分、少し上に上げておく

            foreach (var textLine in formattedLines)
            {
                var lineHeight = (this.FontSize + (leadingBase * this.FontSize));

                // 上に突き抜けてる分を計算してあげないと…
                if (!this.IsLineHeightFixed && textLine.PlacedGlyphs.Any(p => p.Y < 0))
                {
                    lineHeight += textLine.PlacedGlyphs.Where(p => p.Y < 0).DefaultIfEmpty().Max(p => p.Glyph.Height);
                }

                height += lineHeight * this.LineHeight;

                // 表示している文字数が高さに影響するのであればそれに応じる
                if (this.IsHeightDepenedingOnVisibleLength &&
                    (this.VisibleLength != -1 && this.VisibleLength <= textLine.PlacedGlyphs.DefaultIfEmpty().Max(x => x.Index)))
                {
                    break;
                }
            }

            height += (leadingBase * this.FontSize);

            return height;
        }
    }
}
