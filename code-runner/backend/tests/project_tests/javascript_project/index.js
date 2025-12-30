/**
 * @typedef {Object} CalculationResult
 * @property {number} A
 * @property {number} B
 * @property {number} Sum
 * @property {string} Timestamp
 * @property {string} Language
 */

/**
 * Generate a random integer between min and max (inclusive)
 */
function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function calculate() {
  const a = randomInt(1, 100);
  const b = randomInt(1, 100);
  const sum = a + b;

  /** @type {CalculationResult} */
  const result = {
    A: a,
    B: b,
    Sum: sum,
    Timestamp: new Date().toISOString(),
    Language: "JavaScript"
  };

  return result;
}

const result = calculate();
console.log(JSON.stringify(result, null, 2));
