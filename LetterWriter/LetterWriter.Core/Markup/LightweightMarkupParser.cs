using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LetterWriter.Markup
{
    public class LightweightMarkupParser
    {
        protected static readonly Dictionary<string, string> CharacterEntityReferencesTable = new Dictionary<string, string>()
        {
            { "amp", "&" },
            { "apos", "'" },
            { "quot", "\"" },
            { "lt", "<" },
            { "gt", ">" },
        };

        public bool TreatNewLineAsLineBreak { get; set; }

        public LightweightMarkupParser()
        {
            this.TreatNewLineAsLineBreak = false;
        }

        public MarkupNode Parse(string sourceText)
        {
            if (String.IsNullOrEmpty(sourceText))
                return new MarkupNode();

            // ���܂萶�^�ʖڂɃp�[�X������͂��Ȃ��Ńx�^�ɂ��
            var sb = new StringBuilder();
            var pos = 0;
            var state = ParseState.Text;

            var attrValueStart = ' ';

            var rootNode = new MarkupNode();
            var currentNode = rootNode;

            var attrName = "";
            var isCloseTag = false;
            var lineNum = 1;
            var columnNum = 0;
            var charRefAmpStart = -1;

            while (pos < sourceText.Length)
            {
                var c = sourceText[pos++];

                columnNum++;

                if (c == '\n')
                {
                    lineNum++;
                    columnNum = 0;
                }

                // �e�L�X�g
                if (state == ParseState.Text)
                {
                    // �^�O�̊J�n
                    if (c == '<')
                    {
                        if (sb.Length > 0)
                        {
                            currentNode.Children.Add(new TextNode(sb.ToString()));
                        }

                        state = ParseState.TagName;
                        isCloseTag = false;
                        sb.Length = 0;
                        continue;
                    }
                    // & �Ŏn�܂�
                    else if (c == '&')
                    {
                        charRefAmpStart = pos;
                        sb.Append(c);
                        continue;
                    }
                    // &...; �I���
                    else if (c == ';' && charRefAmpStart != -1)
                    {
                        var len = (pos - charRefAmpStart) - 1;
                        if (len != 0)
                        {
                            var charRef = sourceText.Substring(charRefAmpStart, len);
                            sb.Remove(sb.Length - (len + 1), len + 1);
                            sb.Append(ResolveCharacterReference(charRef));
                            charRefAmpStart = -1;
                        }
                        continue;
                    }
                    // ���e
                    else
                    {
                        sb.Append(c);
                    }
                }
                // �J�n�E���^�O�̖��O
                else if (state == ParseState.TagName)
                {
                    // ���^�O?
                    if (sb.Length == 0 && c == '/')
                    {
                        isCloseTag = true;
                        continue;
                    }
                    // �����T��
                    else if (c == ' ' || c == '=' || c == '/')
                    {
                        state = ParseState.TagAttributes;

                        var newElement = new Element(sb.ToString());
                        currentNode.AppendChild(newElement);
                        currentNode = newElement;
                        isCloseTag = false;
                        sb.Length = 0;

                        continue;
                    }
                    // ���������e���Ȃ��^�O�I��
                    else if (c == '>')
                    {
                        state = ParseState.Text;
                        charRefAmpStart = -1;

                        // ���^�O��������߂�
                        if (isCloseTag)
                        {
                            // �߂������ĂȂ�?
                            if (currentNode.Parent == null)
                            {
                                Debug.WriteLine(String.Format("Unmatched Tag: {0}; Line={1}; Column={2}", sb.ToString(), lineNum, columnNum));
                                continue;
                            }
                            // �^�O�̕��͑΂ɂȂ��Ă�?
                            if (((Element) currentNode).TagName != sb.ToString())
                            {
                                Debug.WriteLine(String.Format("Unmatched Tag: {0}; Line={1}; Column={2}", sb.ToString(), lineNum, columnNum));
                                continue;
                            }
                            currentNode = currentNode.Parent;
                        }
                        else
                        {
                            // �v�f�J�n
                            var newElement = new Element(sb.ToString());
                            currentNode.AppendChild(newElement);
                            currentNode = newElement;
                            isCloseTag = false;
                        }

                        sb.Length = 0;
                        continue;
                    }
                    // �^�O�̖��O���ۂ��ۂ�
                    else
                    {
                        sb.Append(c);
                    }
                }
                // ��������
                else if (state == ParseState.TagAttributes)
                {
                    // �I���
                    if (c == '>')
                    {
                        state = ParseState.Text;
                        charRefAmpStart = -1;

                        // ���^�O��������߂�
                        if (isCloseTag)
                        {
                            currentNode = currentNode.Parent;
                        }
                        continue;
                    }
                    // �󔒕����܂��̓R���g���[������
                    else if (Char.IsControl(c) || Char.IsWhiteSpace(c))
                    {
                        continue;
                    }
                    // �f�t�H���g�����I��
                    else if (c == '=')
                    {
                        state = ParseState.TagAttributeValue;
                        sb.Append("value"); // ������value���Ă��Ƃɂ���
                        attrValueStart = ' ';
                        continue;
                    }
                    // ���e��v�f
                    else if (c == '/')
                    {
                        state = ParseState.TagAttributes;
                        isCloseTag = true;
                        continue;
                    }
                    // �������J�n
                    else
                    {
                        state = ParseState.TagAttributeName;
                        sb.Length = 0;
                        sb.Append(c);
                        continue;
                    }
                }
                // ������
                else if (state == ParseState.TagAttributeName)
                {
                    // �����̒l�J�n
                    if ((Char.IsControl(c) || Char.IsWhiteSpace(c)) || c == '=')
                    {
                        attrName = sb.ToString();
                        sb.Length = 0;
                        state = ParseState.TagAttributeNameCompleted;
                        continue;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                // ����������l�܂�
                else if (state == ParseState.TagAttributeNameCompleted)
                {
                    // ����
                    if ((Char.IsControl(c) || Char.IsWhiteSpace(c)))
                    {
                        continue;
                    }
                    // �N�H�[�g
                    else if (c == '"' || c == '\'')
                    {
                        state = ParseState.TagAttributeValue;
                        attrValueStart = c;
                        continue;
                    }
                    // �͂܂�Ă��Ȃ� (�����Ȃ�n�܂�) <tagName=value>...</tagName>
                    else
                    {
                        state = ParseState.TagAttributeValue;
                        attrValueStart = c;
                        pos--;
                        continue;
                    }
                }
                // �����l
                else if (state == ParseState.TagAttributeValue)
                {
                    // �����̒l�̏I���
                    if ((attrValueStart == '"' && c == '"') ||
                        (attrValueStart == '\'' && c == '\'') ||
                        (attrValueStart == ' ' && (Char.IsControl(c) || Char.IsWhiteSpace(c))))
                    {
                        currentNode.Attributes[attrName] = sb.ToString();
                        sb.Length = 0;
                        state = ParseState.TagAttributes;
                        continue;
                    }
                    // & �Ŏn�܂�
                    else if (c == '&')
                    {
                        charRefAmpStart = pos;
                        sb.Append(c);
                        continue;
                    }
                    // &...; �I���
                    else if (c == ';' && charRefAmpStart != -1)
                    {
                        var len = (pos - charRefAmpStart) - 1;
                        if (len != 0)
                        {
                            var charRef = sourceText.Substring(charRefAmpStart, len);
                            sb.Remove(sb.Length - (len + 1), len + 1);
                            sb.Append(ResolveCharacterReference(charRef));
                            charRefAmpStart = -1;
                        }
                        continue;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            // �c��
            if (sb.Length > 0 && state == ParseState.Text)
            {
                currentNode.AppendChild(new TextNode(sb.ToString()));
            }

            return rootNode;
        }

        private static string ResolveCharacterReference(string value)
        {
            if (value.StartsWith("#x"))
            {
                // &#xNNNN; (���l�����Q��)
                var result = 0;
                if (Int32.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result))
                {
                    return new string((char) result, 1);
                }
            }
            else if (value.StartsWith("#"))
            {
                // &#NN; (���l�����Q��)
                var result = 0;
                if (Int32.TryParse(value.Substring(1), System.Globalization.NumberStyles.Integer, null, out result))
                {
                    return new string((char)result, 1);
                }
            }
            else if (CharacterEntityReferencesTable.ContainsKey(value))
            {
                // amp (�������ԎQ��)
                return CharacterEntityReferencesTable[value];
            }

            return String.Empty;
        }

        private enum ParseState
        {
            Text,
            TagName,
            TagAttributes,
            TagAttributeName,
            TagAttributeNameCompleted,
            TagAttributeValue,
        }
    }
}