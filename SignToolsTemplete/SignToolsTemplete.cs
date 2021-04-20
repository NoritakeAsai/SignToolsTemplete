using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using cAlgo.Indicators;

namespace cAlgo {
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SignToolsTemplete : Indicator {
        [Parameter(DefaultValue = 25)]
        public int ShortPeriod { get; set; }
        [Parameter(DefaultValue = 75)]
        public int LongPeriod { get; set; }

        // --- サインタイミング確認用に一応表示
        [Output("ShortMA", LineColor = "Blue")]
        public IndicatorDataSeries LongMA { get; set; }
        [Output("LongMA", LineColor = "SkyBlue")]
        public IndicatorDataSeries ShortMA { get; set; }

        private SimpleMovingAverage _longMa;
        private SimpleMovingAverage _shortMa;


        //--------------------------------------------------
        // Initialize内でサインインジオブジェクトを設定する
        protected override void Initialize() {

            // --- 利用するインジケーターを初期化
            _shortMa = Indicators.SimpleMovingAverage(Bars.ClosePrices, ShortPeriod);
            _longMa = Indicators.SimpleMovingAverage(Bars.ClosePrices, LongPeriod);

            // --- サインインジオブジェクト設定
            var sign = new SignIndicator(this, BuyOrSell);

            // --- 色やアイコン変えたければ変える
             sign.BuyColor = Color.Blue;
             sign.SellColor = Color.Pink;
             sign.BuyIcon = ChartIconType.UpTriangle;
             sign.SellIcon = ChartIconType.DownTriangle;

            // --- 画面色替えアラーム。色を設定すると有効化
            // sign.AlertColor = Color.Yellow;

            // --- 音声アラーム。ファイル名を設定すると有効化。フルパス指定の方が確実。
            // sign.SoundFile = "Alarm01.wav";

            // --- アラートタイミングを変更。デフォルトはUpdate(バー更新後のサイン確定時のみアラート)
            // sign.WhenAlert = WhenAlert.FirstAndUpdate;

        }

        //--------------------------------------------
        //　Calculateでは表示させたいIndicatorの値渡すだけ
        //　(何も表示させないなら空っぽでもいい)
        public override void Calculate(int index) {
            LongMA[index] = _longMa.Result[index];
            ShortMA[index] = _shortMa.Result[index];
        }

        //-------------------------------------------------------------------
        //  サイン判定メソッドはこんな感じで用意
        //     必ずindexを受け取ってTradeTypeかnullを返す形にする
        public TradeType? BuyOrSell(int index) {

            // --- ここで条件判定を行い、買いサイン出すならTradeType.Buy、売りサイン出すならTradeType.Sell、サインなしならnullを返す。
            if (index < 1) return null;
            if (LongMA[index - 1] < ShortMA[index - 1] && LongMA[index] > ShortMA[index]) {
                return TradeType.Sell;
            } else if (LongMA[index - 1] > ShortMA[index - 1] && LongMA[index] < ShortMA[index]) {
                return TradeType.Buy;
            } else {
                return null;
            }
        }
    }

    //==========================================
    //
    //　サイン表示用クラス
    //   これより下はそのままでいい
    //
    //===========================================
    [Flags] public enum WhenAlert { None = 0, FirstTime = 1, Update = 2, FirstAndUpdate = 3, EveryTime = 4 }
    public class SignIndicator {

        // --- Set可能プロパティ
        public ChartIconType BuyIcon { get; set; }
        public ChartIconType SellIcon { get; set; }
        public Color BuyColor { get; set; }
        public Color SellColor { get; set; }
        public double DrawMargin { get; set; }
        public int JumpMargin { get; set; }
        public WhenAlert WhenAlert { get; set; }
        public string SoundFile {
            set {
                if (value == "") _soundFile = "";
                if (!value.Contains("\\")) {
                    _soundFile = "C:\\Windows\\Media\\" + value;
                }
                if (!value.Contains(".")) {
                    _soundFile += ".wav";
                }
            }
        }
        public Color AlertColor {
            get { return _alertRect.FillColor; }
            set { _alertRect.FillColor = value; }
        }

        // --- Get可能プロパティ
        public WrapPanel ButtonPanel { get; private set; }

        // --- field
        private readonly Chart _chart;
        private readonly Func<int, TradeType?> _JudgeFunc = null;
        private readonly bool _isVisualMode;
        private readonly INotifications _notifications;
        private readonly Rectangle _alertRect;
        private string _soundFile;
        private List<DateTime> _signOpenTimes;
        private bool _firstTime = false;
        private bool _IsInit = false;

        //------------------
        // コンストラクタ
        public SignIndicator(Algo algo, Func<int, TradeType?> JudgeFunc, bool showButton = true) {
            _signOpenTimes = new List<DateTime>();
            _chart = algo.Chart;
            _notifications = algo.Notifications;
            _isVisualMode = (algo.RunningMode == RunningMode.VisualBacktesting);
            BuyIcon = ChartIconType.UpArrow;
            SellIcon = ChartIconType.DownArrow;
            var colors = _chart.ColorSettings;
            BuyColor = colors.BuyColor;
            SellColor = colors.SellColor;
            DrawMargin = (_chart.TopY - _chart.BottomY) / 20;
            JumpMargin = 1;
            SoundFile = "";
            WhenAlert = WhenAlert.Update;
            _JudgeFunc = JudgeFunc;

            // ---サイン時チャートに色付け用
            _alertRect = new Rectangle {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                FillColor = Color.Transparent,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            _chart.AddControl(_alertRect);
            _chart.KeyDown += _ => _alertRect.IsVisible = false;

            // --- ボタン使うなら作る
            _CreateButtonPanel();
            if(showButton) _chart.AddControl(ButtonPanel);

            // ---　値動き時判定
            _chart.Bars.Tick += OnTick;
            _chart.Bars.BarOpened += _ => _FixSign(_chart.Bars.Count - 2);
        }

        //-----  以下　private ----
        //-----------------
        // 値動き時処理
        private void OnTick(BarsTickEventArgs args) {
            // --- 起動後最初の値動きで過去分作成
            if (!_IsInit) {
                if(_JudgeFunc == null) {
                    throw new InvalidOperationException();
                }
                for (int i = 0; i < _chart.Bars.Count - 1; i++) {
                    var res = _JudgeFunc(i);
                    if (res != null) {
                        _DrawSign(i, (TradeType)res);
                        _FixSign(i);
                    }
                }
                _IsInit = true;
            }

            // --- 未確定でサイン描画
            int index = _chart.Bars.Count - 1;
            if (_JudgeFunc != null) {
                var res = _JudgeFunc(index);
                if (res != null) {
                    _DrawSign(index, (TradeType)res);
                }
            } else {
                _RemoveSign(index);
            }
        }

        //-----------------
        // サイン確定処理
        private void _FixSign(int index) {
            var sign = _chart.FindObject(_GetName(index)) as ChartIcon;
            if (sign != null) {
                // --- リスト追加
                var time = _chart.Bars.OpenTimes[index];
                _signOpenTimes.Add(time);

                // --- Y位置調整
                var upper = _chart.Bars[index].High + DrawMargin;
                var lower = _chart.Bars[index].Low - DrawMargin;
                var itype = sign.IconType;

                if (itype == BuyIcon) {
                    sign.Y = lower;
                } else if (itype == SellIcon) {
                    sign.Y = upper;
                }
                if (WhenAlert.HasFlag(WhenAlert.Update)) {
                    _Alert();
                }
            }
            _firstTime = false;
        }

        //--------------
        // ボタン作成
        private void _CreateButtonPanel() {
            Func<Panel, string, Button> addButton = (panel, text) => {
                var button = new Button { Text = text, FontSize = 16, Margin = 5 };
                panel.AddChild(button);
                return button;
            };
            ButtonPanel = new WrapPanel {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = 10,
            };
            var oldestJump = addButton(ButtonPanel, "◀◀");
            var prevJump = addButton(ButtonPanel, " ◀ ");
            var nextJump = addButton(ButtonPanel, " ▶ ");
            var recentJump = addButton(ButtonPanel, "▶▶");

            prevJump.Click += _ => {
                var leftTime = _chart.Bars.OpenTimes[_chart.LastVisibleBarIndex - JumpMargin];
                var jumpPos = _signOpenTimes.BinarySearch(leftTime);
                if (jumpPos < 0) jumpPos = ~jumpPos;
                _ScrollTo(_signOpenTimes[jumpPos - 1]);
            };

            nextJump.Click += _ => {
                var leftTime = _chart.Bars.OpenTimes[_chart.LastVisibleBarIndex - JumpMargin + 1];
                var jumpPos = _signOpenTimes.BinarySearch(leftTime);
                if (jumpPos < 0) jumpPos = ~jumpPos;
                else jumpPos += 1;
                _ScrollTo(_signOpenTimes[jumpPos]);
            };
            oldestJump.Click += _ => _ScrollTo(0);
            recentJump.Click += _ => _ScrollTo(_chart.Bars.Count + 20);
        }

        //---------------------
        // 右側基準でスクロール
        private void _ScrollTo(DateTime time) {
            _ScrollTo(_chart.Bars.OpenTimes.GetIndexByTime(time));
        }
        private void _ScrollTo(int index) {
            index -= _chart.MaxVisibleBars;
            index = (index < 0 ? 0 : index);
            index = (index >= _chart.Bars.Count ? _chart.Bars.Count - 1 : index);
            _chart.ScrollXTo(index + JumpMargin);

            var highest = _chart.Bars.HighPrices.Skip(index).Take(_chart.MaxVisibleBars).Max();
            var lowest = _chart.Bars.LowPrices.Skip(index).Take(_chart.MaxVisibleBars).Min();
            var margin = (highest - lowest) / 20;
            _chart.SetYRange(lowest - margin, highest + margin);
        }

        //--------------
        // サイン描く
        private void _DrawSign(int index, TradeType tradeType) {
            var iconType = tradeType == TradeType.Buy ? BuyIcon : SellIcon;
            var iconColor = tradeType == TradeType.Buy ? BuyColor : SellColor;
            var time = _chart.Bars.OpenTimes[index];
            var price = _chart.Bars.LastBar.Close;
            _chart.DrawIcon(_GetName(index), iconType, time, price, iconColor);

            // --- アラート処理は最新バーのみ
            if (index == _chart.Bars.Count - 1) {
                if ((WhenAlert.HasFlag(WhenAlert.FirstTime) && !_firstTime) || WhenAlert == WhenAlert.EveryTime) {
                    _Alert();
                    _firstTime = true;
                }
            }

            // --- cTraner連携用仕掛け
            if (_isVisualMode) {
                _chart.DrawIcon("entry_sign_for_ctraner", iconType, index, 0, Color.Transparent).IsInteractive = true;
            }
        }

        //----------------
        // サイン消す
        private void _RemoveSign(int index) {
            var name = _GetName(index);
            _chart.RemoveObject(name);
            _alertRect.IsVisible = false;
        }

        //----------------
        // サインオブジェクト名
        private string _GetName(int index) {
            var time = _chart.Bars.OpenTimes[index];
            return "sign_at_" + time.ToString("G").ToString();
        }

        //-----------------------
        // アラート（音と画面色）
        private void _Alert() {
            _alertRect.IsVisible = true;
            if (_soundFile != "") {
                _notifications.PlaySound(_soundFile);
            }
        }
    }
}
