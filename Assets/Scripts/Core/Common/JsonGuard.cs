using System.Collections.Generic;

namespace Murim.Core.Common
{
    /// <summary>
    /// Iterative JSON nesting guard that measures object/array depth outside string literals.
    /// </summary>
    public static class JsonGuard
    {
        /// <summary>
        /// Returns the maximum nesting depth of <c>{</c>/<c>[</c> outside string literals,
        /// or <c>-1</c> if the input is null, empty, has an unclosed string, or has unbalanced brackets.
        /// </summary>
        public static int MaxDepth(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return -1;
            }

            var stack = new Stack<char>();
            int maxDepth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (!SkipString(text, ref i))
                    {
                        return -1;
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    stack.Push(c == '{' ? '}' : ']');
                    if (stack.Count > maxDepth)
                    {
                        maxDepth = stack.Count;
                    }
                }
                else if (c == '}' || c == ']')
                {
                    if (stack.Count == 0 || stack.Pop() != c)
                    {
                        return -1;
                    }
                }
            }

            return stack.Count == 0 ? maxDepth : -1;
        }

        private static bool SkipString(string text, ref int index)
        {
            index++;
            while (index < text.Length)
            {
                char c = text[index];
                if (c == '\\')
                {
                    index += 2;
                    continue;
                }

                if (c == '"')
                {
                    return true;
                }

                index++;
            }

            return false;
        }
    }
}
