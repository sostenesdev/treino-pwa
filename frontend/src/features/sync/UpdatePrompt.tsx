import { useRegisterSW } from "virtual:pwa-register/react";

export function UpdatePrompt() {
  const {
    needRefresh: [available, setAvailable],
    updateServiceWorker,
  } = useRegisterSW();
  if (!available) return null;
  return (
    <aside className="notice" role="status">
      Uma atualização está disponível. Seus treinos salvos serão preservados.{" "}
      <button onClick={() => updateServiceWorker(true)}>
        Atualizar aplicativo
      </button>{" "}
      <button className="text" onClick={() => setAvailable(false)}>
        Depois
      </button>
    </aside>
  );
}
