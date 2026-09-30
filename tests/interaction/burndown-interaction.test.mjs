import test from 'node:test';
import assert from 'node:assert/strict';
import { nearestPoint, tooltipPosition } from '../../src/TimeOps.Web/wwwroot/burndown-interaction.mjs';

const points = [{ x: 70, y: 280 }, { x: 100, y: 42 }, { x: 130, y: 200 }];

test('consults the same date across the entire plot height, including a steep segment', () => {
    for (const y of [42, 100, 180, 286]) {
        assert.equal(nearestPoint(points, 90, y, 30), points[1]);
    }
    assert.equal(nearestPoint(points, 84, 160, 30), points[0]);
    assert.equal(nearestPoint(points, 86, 160, 30), points[1]);
});

test('does not invent actual values in the future or outside the plot', () => {
    for (const [x, y] of [[69, 100], [893, 100], [100, 41], [100, 287], [146, 100]]) {
        assert.equal(nearestPoint(points, x, y, 30), null);
    }
    assert.equal(nearestPoint([], 100, 100, 30), null);
    assert.equal(nearestPoint([points[0]], 70, 100, 30), points[0]);
});

test('handles dense dates without gaps between points', () => {
    const dense = Array.from({ length: 63 }, (_, index) => ({ x: 70 + index * 822 / 62 }));
    for (let x = 70; x <= 892; x += 0.5) assert.ok(nearestPoint(dense, x, 100, 822 / 62));
});

test('moves continuously within a date and keeps the popup within the chart', () => {
    const size = { width: 248, height: 168 };
    const bounds = { width: 1000, height: 370 };
    const first = tooltipPosition({ x: 400, y: 300 }, size, bounds);
    const next = tooltipPosition({ x: 401, y: 301 }, size, bounds);
    assert.equal(next.x - first.x, 1);
    assert.equal(next.y - first.y, 1);
    for (const pointer of [{ x: 0, y: 0 }, { x: 1000, y: 370 }]) {
        const position = tooltipPosition(pointer, size, bounds);
        assert.ok(position.x >= 8 && position.x + size.width <= bounds.width - 8);
        assert.ok(position.y >= 8 && position.y + size.height <= bounds.height - 8);
    }
});
