import React, { useState, useEffect } from 'react';

// Helper: build the "mask" equivalent as a regex based on unitDecml
const getQtyRegex = (unitDecml) => {
  return unitDecml > 0
    ? new RegExp(`^\\d{0,5}(\\.\\d{0,${unitDecml}})?$`) // e.g. #####.###
    : /^\d{0,5}$/;                                       // e.g. #####
};

// Helper: format a numeric qty to the fixed decimal places (like ToString(mask) in WPF)
const formatQty = (qty, unitDecml) => {
  const num = Number(qty) || 0;
  return unitDecml > 0 ? num.toFixed(unitDecml) : String(Math.trunc(num));
};

function QtyInput({ dist, unitDecml, onQtyChange }) {
  const [localValue, setLocalValue] = useState(formatQty(dist.qty, unitDecml));
  const [isFocused, setIsFocused] = useState(false);

  // Keep in sync if qty changes externally (e.g. via +/- buttons) while not editing
  useEffect(() => {
    if (!isFocused) setLocalValue(formatQty(dist.qty, unitDecml));
  }, [dist.qty, unitDecml, isFocused]);

  const handleChange = (e) => {
    const val = e.target.value;
    const regex = getQtyRegex(unitDecml);
    if (val === '' || regex.test(val)) {
      setLocalValue(val);
      // Push every committed keystroke up so the parent (DetailModal grandTotal) updates live.
      // When the field is empty (user cleared digits mid-edit, e.g. backspacing from "5" to ""),
      // fall back to 0 so grandTotal reflects the cleared state. On blur the value will be
      // clamped to a valid numeric (never NaN/null).
      const numericVal = val === '' ? 0 : parseFloat(val);
      onQtyChange(dist, isNaN(numericVal) ? 0 : numericVal);
    }
  };

  const handleBlur = () => {
    setIsFocused(false);
    let num = parseFloat(localValue);
    if (isNaN(num)) num = 0;
    num = parseFloat(num.toFixed(unitDecml)); // clamp precision
    setLocalValue(formatQty(num, unitDecml));
    onQtyChange(dist, num); // push the real numeric value up to parent state
  };

  return (
    <input
      type="text"
      inputMode="decimal"
      value={localValue}
      onFocus={() => setIsFocused(true)}
      onChange={handleChange}
      onBlur={handleBlur}
      onFocus={(e) => e.target.select()} // select all text on focus for easier editing
      className="w-15 text-center font-bold text-slate-800 text-lg bg-transparent border-none focus:outline-none focus:ring-1 focus:ring-slate-400 rounded"
    />
  );
}

export default QtyInput;