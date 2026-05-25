using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.AudioSplitter.UI
{
    /// <summary>
    /// Option behaviour inside SubMenu, basically
    /// </summary>
    /// <typeparam name="T">Type of stored data</typeparam>
    public class DropdownMenu<T> : TextMenuExt.SubMenu
    {
        public class Option : IEquatable<Option>
        {
            public string Label;
            public T Value;

            public Option(string label, T value)
            {
                Label = label;
                Value = value;
            }

            public bool Equals(Option other) => Value.Equals(other.Value);
        }

        private int index = 0;
        public int OptionIndex
        {
            get => index;
            set
            {
                if (value >= 0 && value < Options.Count)
                {
                    index = value;
                    CurrentOption = Options[index];
                }
            }
        }
        public List<Option> Options = new();
        public Option CurrentOption = null;

        public Action<T> OnOptionChange;

        // Why aren't Icon and ease protected? :thinking:
        private FieldInfo arrowIconInfo = null;
        private FieldInfo easeInfo = null;

        protected MTexture arrowIcon
        {
            get
            {
                if (arrowIconInfo == null)
                    arrowIconInfo = typeof(TextMenuExt.SubMenu).GetField("Icon", BindingFlags.Instance | BindingFlags.NonPublic);
                return (MTexture)arrowIconInfo.GetValue(this);
            }
        }

        protected float ease
        {
            get
            {
                if (easeInfo == null)
                    easeInfo = typeof(TextMenuExt.SubMenu).GetField("ease", BindingFlags.Instance | BindingFlags.NonPublic);
                return (float)easeInfo.GetValue(this);
            }
        }

        public DropdownMenu(string label) : base(label, false) { }

        public DropdownMenu<T> Add(string label, T value, bool selected = false)
        {
            var option = new Option(label, value);
            Options.Add(option);

            if (CurrentOption == null)
                OptionIndex = 0;

            Item item = new(option);
            int itemPosition = Options.Count - 1;
            item.Pressed(() =>
            {
                OptionIndex = itemPosition;
                OnOptionChange(CurrentOption.Value);
                Exit();
            });
            base.Add(item);

            if (selected)
                OptionIndex = itemPosition;

            return this;
        }

        public override void Added()
        {
            this.Container.InnerContent = TextMenu.InnerContentMode.TwoColumn;
            base.Added();
        }

        public DropdownMenu<T> Change(Action<T> action)
        {
            OnOptionChange = action;
            return this;
        }

        public new void Clear()
        {
            base.Clear();
            Options.Clear();
        }

        public override float LeftWidth() => MultiLanguageFont.Measure(Label).X;
        public override float RightWidth() => MultiLanguageFont.Measure(GetOptionLabel()).X + arrowIcon.Width;

        private string GetOptionLabel()
        {
            string label = CurrentOption.Label ?? "";

            float width = MultiLanguageFont.Measure(label).X;
            float maxWidth = Container.Width - LeftWidth() - 96f;

            if (width > maxWidth)
            {
                int length = label.Length;

                while (length > 0 && MultiLanguageFont.Measure(label + "...").X > maxWidth)
                    label = label.Substring(0, --length);
                label += "...";
            }

            return label;
        }

        public override void Render(Vector2 position, bool highlighted)
        {
            Vector2 top = new Vector2(position.X, position.Y - (Height() / 2));

            float alpha = Container.Alpha;
            Color color = Disabled ? Color.DarkSlateGray : ((highlighted ? Container.HighlightColor : Color.White) * alpha);
            Color strokeColor = Color.Black * (alpha * alpha * alpha);

            bool uncentered = Container.InnerContent == TextMenu.InnerContentMode.TwoColumn && !AlwaysCenter;

            Vector2 titlePosition = top + (Vector2.UnitY * TitleHeight / 2) + (uncentered ? Vector2.Zero : new Vector2(Container.Width * 0.5f, 0f));
            Vector2 justify = uncentered ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            ActiveFont.DrawOutline(Label, titlePosition, justify, Vector2.One, color, 2f, strokeColor);

            Vector2 optionPosition = titlePosition + new Vector2(Container.Width - RightWidth() - arrowIcon.Width - 10f, 0);
            Color itemColor = Options.Contains(CurrentOption) ? color : Color.DarkSlateGray;
            MultiLanguageFont.DrawOutline(GetOptionLabel(), optionPosition, justify, Vector2.One, itemColor, 2f, strokeColor);

            Vector2 iconJustify = uncentered ? new Vector2(MultiLanguageFont.Measure(GetOptionLabel()).X + arrowIcon.Width, 5f) : new Vector2(MultiLanguageFont.Measure(GetOptionLabel()).X / 2 + arrowIcon.Width, 5f);
            arrowIcon.DrawOutlineCentered(
                optionPosition + iconJustify,
                (Disabled || Items.Count < 1 ? Color.DarkSlateGray : (Focused ? Container.HighlightColor : Color.White)) * alpha
            );

            if (Focused && ease > 0.9f)
            {
                Vector2 menuPosition = new Vector2(top.X + ItemIndent, top.Y + TitleHeight + ItemSpacing);
                RecalculateSize();
                foreach (TextMenu.Item item in Items)
                {
                    if (item.Visible)
                    {
                        float height = item.Height();
                        Vector2 itemPosition = menuPosition + new Vector2(0f, height * 0.5f + item.SelectWiggler.Value * 8f);
                        if (itemPosition.Y + height * 0.5f > 0f && itemPosition.Y - height * 0.5f < Engine.Height)
                        {
                            item.Render(itemPosition, Focused && Current == item);
                        }
                        menuPosition.Y += height + ItemSpacing;
                    }
                }
            }
        }

        internal class Item : TextMenu.Button
        {
            public Option Option;

            public Item(Option option) : base(option.Label)
            {
                Option = option;
            }

            public override float LeftWidth() => 0f;
            public override float RightWidth() => MultiLanguageFont.Measure(Label).X;

            public override void Render(Vector2 position, bool highlighted)
            {
                float alpha = Container.Alpha;
                Color color = (Disabled ? Color.DarkSlateGray : ((highlighted ? Container.HighlightColor : Color.White) * alpha));
                Color strokeColor = Color.Black * (alpha * alpha * alpha);

                position += new Vector2(Container.Width - RightWidth(), 0);
                MultiLanguageFont.DrawOutline(Label, position, new Vector2(0f, 0.5f), Vector2.One, color, 2f, strokeColor);
            }
        }
    }
}
