<template>
  <div class="typing" role="status" aria-live="polite">
    <span class="visually-hidden">Assistenten skriver</span>
    <span class="typing__dot" aria-hidden="true"></span>
    <span class="typing__dot" aria-hidden="true"></span>
    <span class="typing__dot" aria-hidden="true"></span>
  </div>
</template>

<style scoped>
.typing {
  display: inline-flex;
  gap: 6px;
  align-items: center;
  padding: 14px 18px;
  border-radius: var(--radius-card);
  border-bottom-left-radius: 6px;
  background: var(--color-bg);
  box-shadow: var(--shadow-md);
}

.typing__dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-secondary-1);
  animation: pulse 1.2s ease-in-out infinite;
}

.typing__dot:nth-child(3) {
  animation-delay: 0.15s;
}

.typing__dot:nth-child(4) {
  animation-delay: 0.3s;
}

@keyframes pulse {
  0%,
  80%,
  100% {
    opacity: 0.25;
    transform: translateY(0);
  }
  40% {
    opacity: 1;
    transform: translateY(-3px);
  }
}
</style>
