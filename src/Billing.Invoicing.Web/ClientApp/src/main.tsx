import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import ConnectivityBanner from './components/ConnectivityBanner';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ConnectivityBanner visible={false} />
  </StrictMode>,
);
