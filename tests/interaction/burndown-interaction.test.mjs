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

test('selects the earlier date at an exact midpoint and includes plot edges', () => {
    assert.equal(nearestPoint(points, 85, 42, 30), points[0]);
    assert.equal(nearestPoint(points, 85.001, 42, 30), points[1]);
    assert.equal(nearestPoint(points, 115, 286, 30), points[1]);
    assert.equal(nearestPoint(points, 115.001, 286, 30), points[2]);
    assert.equal(nearestPoint(points, 145, 100, 30), points[2]);
    assert.equal(nearestPoint(points, 145.001, 100, 30), null);

    const edges = [{ x: 70 }, { x: 892 }];
    assert.equal(nearestPoint(edges, 70, 42, 822), edges[0]);
    assert.equal(nearestPoint(edges, 892, 286, 822), edges[1]);
});

test('selects by the actual spacing when dates have unequal distances', () => {
    const uneven = [{ x: 70 }, { x: 80 }, { x: 130 }, { x: 200 }];
    assert.equal(nearestPoint(uneven, 76, 100, 70), uneven[1]);
    assert.equal(nearestPoint(uneven, 104, 100, 70), uneven[1]);
    assert.equal(nearestPoint(uneven, 106, 100, 70), uneven[2]);
    assert.equal(nearestPoint(uneven, 166, 100, 70), uneven[3]);
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
