import { defineConfig } from 'vitest/config';

/**
 * Settings for the browser code's tests (`npm test`).
 *
 * The code under test takes the browser's features as inputs, so the tests pass in small pretend versions and
 * run in plain Node, without a pretend browser page.
 */
export default defineConfig({
    test: {
        include: ['**/*.test.ts'],
        exclude: ['node_modules/**'],
        environment: 'node',
        restoreMocks: true,
    },
});
