using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Uno.Kanban.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using Windows.System;

namespace Flowery.Uno.RuntimeTests.Controls
{
    /// <summary>Two fingers and Ctrl with the wheel zoom the board through its sizes.</summary>
    [TestClass]
    public class Given_KanbanPinch
    {
        [TestMethod]
        public async Task When_TwoFingersSpread_TheBoardZoomsInOneSizePerStep_UpToTheLargest()
        {
            var kanban = await CreateAsync();

            // One step is a quarter more distance; 1.2 is not yet a step.
            Pinch(kanban, from: 50, to: 60);
            Assert.AreEqual(DaisySize.Medium, kanban.BoardSize);
            Pinch(kanban, from: 50, to: 63);
            Assert.AreEqual(DaisySize.Large, kanban.BoardSize);
            // Far beyond two steps, the board stops at its largest size.
            Pinch(kanban, from: 50, to: 400);
            Assert.AreEqual(DaisySize.ExtraLarge, kanban.BoardSize);
        }

        [TestMethod]
        public async Task When_TwoFingersPinch_TheBoardZoomsOut_DownToTheSmallest()
        {
            var kanban = await CreateAsync();

            Pinch(kanban, from: 100, to: 79);
            Assert.AreEqual(DaisySize.Small, kanban.BoardSize);
            Pinch(kanban, from: 400, to: 20);
            Assert.AreEqual(DaisySize.ExtraSmall, kanban.BoardSize);
        }

        [TestMethod]
        public async Task When_APinchStartsOnACard_TheCardKeepsItsSelectionAndIsNotDragged()
        {
            var kanban = await CreateAsync();
            var tasks = kanban.Board.Columns.SelectMany(static column => column.Tasks).ToList();
            tasks[1].IsSelected = true;

            // The first finger presses the card, which selects it alone, as FlowTaskCard does on a press.
            kanban.RememberSelectionBeforeTouch();
            foreach (var task in tasks)
                task.IsSelected = task == tasks[0];
            kanban.Pinch.Press(1, new Point(100, 200));
            Assert.IsFalse(kanban.IsPinchTouch, "One finger may still drag the card.");
            kanban.Pinch.Press(2, new Point(180, 200));

            Assert.IsTrue(kanban.IsPinchTouch, "A card must not start a drag while fingers pinch.");
            Assert.IsFalse(tasks[0].IsSelected, "The pinch selected the card under its first finger.");
            Assert.IsTrue(tasks[1].IsSelected, "The pinch changed the selection of another card.");

            // Until the next touch, the fingers drag nothing; the next touch selects as before.
            kanban.Pinch.Lift(2);
            Assert.IsTrue(kanban.IsPinchTouch);
            kanban.Pinch.Lift(1);
            Assert.IsTrue(kanban.IsPinchTouch);
            kanban.RememberSelectionBeforeTouch();
            tasks[1].IsSelected = false;
            kanban.Pinch.Press(3, new Point(100, 200));
            Assert.IsFalse(kanban.IsPinchTouch, "The next touch may drag a card again.");
            kanban.Pinch.Press(4, new Point(180, 200));
            Assert.IsTrue(tasks[1].IsSelected, "The next pinch kept the selection of its first finger.");
        }

        [TestMethod]
        public async Task When_CtrlAndTheWheelTurn_TheBoardChangesOneSizePerNotch()
        {
            var kanban = await CreateAsync();

            Assert.IsFalse(kanban.ZoomByWheel(VirtualKeyModifiers.None, 120), "The wheel alone scrolls.");
            Assert.AreEqual(DaisySize.Medium, kanban.BoardSize);
            Assert.IsTrue(kanban.ZoomByWheel(VirtualKeyModifiers.Control, 120));
            Assert.AreEqual(DaisySize.Large, kanban.BoardSize);
            Assert.IsTrue(kanban.ZoomByWheel(VirtualKeyModifiers.Control, -120));
            Assert.IsTrue(kanban.ZoomByWheel(VirtualKeyModifiers.Control, -120));
            Assert.AreEqual(DaisySize.Small, kanban.BoardSize);
        }

        private static async Task<FlowKanban> CreateAsync()
        {
            var kanban = new FlowKanban
            {
                Width = 900,
                Height = 600,
                Board = new FlowKanbanData
                {
                    Columns =
                    {
                        new FlowKanbanColumnData { Title = "Todo", Tasks = { new FlowTask { Title = "First" }, new FlowTask { Title = "Second" } } }
                    }
                }
            };
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);
            Assert.AreEqual(DaisySize.Medium, kanban.BoardSize);
            return kanban;
        }

        // Two fingers on both sides of a point, from and to that distance from it, then both lift.
        private static void Pinch(FlowKanban kanban, double from, double to)
        {
            var middle = new Point(450, 300);
            kanban.Pinch.Press(1, new Point(middle.X - from, middle.Y));
            kanban.Pinch.Press(2, new Point(middle.X + from, middle.Y));
            for (var step = 1; step <= 4; step++)
            {
                var distance = from + (to - from) * step / 4;
                kanban.Pinch.Move(1, new Point(middle.X - distance, middle.Y));
                kanban.Pinch.Move(2, new Point(middle.X + distance, middle.Y));
            }
            kanban.Pinch.Lift(1);
            kanban.Pinch.Lift(2);
        }
    }
}
