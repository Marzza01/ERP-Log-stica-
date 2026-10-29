/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldAlert, ArrowLeft } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

export default function SinAccesoPage() {
  const navigate = useNavigate();
  const { usuario } = useAuth();

  const handleVolver = () => {
    if (usuario?.rol === 'Comprador') {
      navigate('/checkout');
    } else {
      navigate('/dashboard');
    }
  };

  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: '60vh',
      textAlign: 'center',
      padding: '2rem',
    }}>
      <div style={{
        width: 72,
        height: 72,
        borderRadius: '50%',
        background: 'rgba(239, 68, 68, 0.12)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--red)',
        marginBottom: '1.5rem',
      }}>
        <ShieldAlert size={36} />
      </div>

      <h1 style={{
        fontSize: '1.75rem',
        fontWeight: 800,
        color: 'var(--text-primary)',
        margin: '0 0 0.5rem 0',
      }}>
        Acceso no autorizado
      </h1>

      <p style={{
        maxWidth: 480,
        color: 'var(--text-secondary)',
        fontSize: '0.95rem',
        lineHeight: 1.5,
        margin: '0 0 1.5rem 0',
      }}>
        Tu rol actual ({usuario?.rol ?? 'Sin rol'}) no cuenta con los permisos necesarios para acceder a este módulo. Si consideras que se trata de un error, solicita los privilegios correspondientes a un Administrador del sistema.
      </p>

      <button
        onClick={handleVolver}
        className="btn btn-primary"
        style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}
      >
        <ArrowLeft size={16} />
        Volver al inicio permitido
      </button>
    </div>
  );
}
